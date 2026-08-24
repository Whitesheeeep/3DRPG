using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Character.State;
using UnityEngine;
using WS_Modules.GAS.Generated;
using WS_Modules.Singleton;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 玩家的交互编排组件，负责从 Provider 收集、筛选、排序并维护当前 Option 选择。
    /// </summary>
    [DefaultExecutionOrder(-700)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractionDetector))]
    public sealed class PlayerInteractor : SingletonMonoBase<PlayerInteractor>
    {
        #region 序列化引用与状态

        [SerializeField, Tooltip("用于评估交互视野的摄像机，一般就是主摄像机。")]
        private Camera viewCamera;
        [SerializeField, Tooltip("用于遮挡检测的图层掩码。")]
        private LayerMask occlusionMask = ~0;
        [SerializeField] private InteractionDetector detector;
        [SerializeField] private bool startDetect = true;

        private readonly List<InteractionOption> options = new();
        private readonly List<InteractionOption> collectedOptions = new();
        private readonly HashSet<InteractionOptionId> optionIds = new();

        // 用于在刷新 Option 时缓存评分数据，避免排序比较器重复计算空间数据。
        private readonly List<ScoredOption> scoredOptions = new();
        private readonly List<InteractionOptionId> previousOptionIds = new();
        private RaycastHit[] occlusionHits = new RaycastHit[16];
        // 用于在 Update 中按玩家控制器之后消费 Intent，避免在同一帧中被控制器重置。
        private PlayerStateBlackboard stateBlackboard;

        #endregion

        #region 事件与属性

        /// <summary>玩家交互单例建立或销毁时触发，供跨场景 UI 组合根重新绑定模型。</summary>
        public static event Action<PlayerInteractor> InstanceChanged;

        /// <summary>最终可用 Option 列表发生变化时触发。</summary>
        public event Action<IReadOnlyList<InteractionOption>> OptionsChanged;

        /// <summary>当前选中 Option 发生变化时触发。</summary>
        public event Action<InteractionOption> SelectionChanged;

        /// <summary>获取当前经过筛选和排序的只读 Option 列表。</summary>
        public IReadOnlyList<InteractionOption> Options => options;

        /// <summary>获取当前选中的 Option；没有可用选项时为空。</summary>
        public InteractionOption SelectedOption { get; private set; }

        /// <summary>获取玩家侧交互检测器。</summary>
        public InteractionDetector Detector => detector;

        #endregion

        #region Unity 生命周期

        /// <summary>注册玩家单例、解析同节点依赖并缓存玩家帧级 Intent 黑板。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            if (detector == null) detector = GetComponent<InteractionDetector>();
            if (viewCamera == null) viewCamera = Camera.main;

            PlayerController playerController = GetComponent<PlayerController>();
            stateBlackboard = playerController != null ? playerController.StateBlackboard : null;
            InstanceChanged?.Invoke(this);
        }

        /// <summary>清空玩家单例并通知窗口 Controller 解除模型绑定。</summary>
        protected override void OnDestroy()
        {
            bool isCurrentInstance = Instance == this;
            base.OnDestroy();
            if (isCurrentInstance) InstanceChanged?.Invoke(null);
        }

        /// <summary>订阅检测结果并按组件配置启动范围检测。</summary>
        private void OnEnable()
        {
            if (detector != null) detector.ProvidersChanged += OnProvidersChanged;
            if (startDetect) StartDetect();
        }

        /// <summary>解绑检测事件、暂停检测并清理当前交互状态。</summary>
        private void OnDisable()
        {
            if (detector != null) detector.ProvidersChanged -= OnProvidersChanged;
            PauseDetect();
        }

        /// <summary>每帧刷新摄像机相关筛选，并在玩家控制器之后处理 Interaction Intent。</summary>
        private void Update()
        {
            if (!startDetect) return;
            RefreshOptions();
            ConsumeInputIntents();
        }

        #endregion

        #region 检测控制

        /// <summary>开启交互检测并立即刷新 Option 列表。</summary>
        public void StartDetect()
        {
            startDetect = true;
            detector.StartDetect();
            RefreshOptions();
        }

        /// <summary>暂停交互检测并清空 Option 与选择状态。</summary>
        public void PauseDetect()
        {
            startDetect = false;
            if (detector != null && detector.IsDetecting) detector.PauseDetect();
            ClearOptions();
        }

        /// <summary>响应检测器 Provider 变化，立即重建候选 Option。</summary>
        /// <param name="providers">最新的 Provider 集合。</param>
        private void OnProvidersChanged(IReadOnlyList<IInteractable> providers) => RefreshOptions();

        #endregion

        #region 选择与执行

        /// <summary>按稳定 Option ID 选择当前列表中的交互项。</summary>
        /// <param name="optionId">待选择的交互选项 ID。</param>
        /// <returns>Option 仍存在于当前列表时返回 true。</returns>
        public bool Select(InteractionOptionId optionId)
        {
            for (int index = 0; index < options.Count; index++)
            {
                InteractionOption option = options[index];
                if (option.Id != optionId) continue;

                // SetSelectedOption 负责抑制重复事件；这里的返回值表示目标仍可选择，便于 UI 随后提交。
                SetSelectedOption(option);
                return true;
            }

            return false;
        }

        /// <summary>选择前一条 Option，并在选择发生变化时返回 true。</summary>
        /// <returns>选择发生变化时返回 true。</returns>
        public bool SelectPrevious()
        {
            if (options.Count == 0) return false;
            int currentIndex = FindSelectedIndex();
            int nextIndex = currentIndex <= 0 ? options.Count - 1 : currentIndex - 1;
            return SetSelectedOption(options[nextIndex]);
        }

        /// <summary>选择后一条 Option，并在选择发生变化时返回 true。</summary>
        /// <returns>选择发生变化时返回 true。</returns>
        public bool SelectNext()
        {
            if (options.Count == 0) return false;
            int currentIndex = FindSelectedIndex();
            int nextIndex = currentIndex < 0 || currentIndex >= options.Count - 1 ? 0 : currentIndex + 1;
            return SetSelectedOption(options[nextIndex]);
        }

        /// <summary>尝试执行当前选中的 Option。</summary>
        /// <returns>当前存在选项且业务执行成功时返回 true。</returns>
        public bool SubmitSelected()
        {
            return SelectedOption != null && SelectedOption.TryExecute(gameObject);
        }

        /// <summary>查找当前选中 Option 的列表索引。</summary>
        /// <returns>找到时返回索引，否则返回 -1。</returns>
        private int FindSelectedIndex()
        {
            if (SelectedOption == null) return -1;
            for (int index = 0; index < options.Count; index++)
                if (options[index].Id == SelectedOption.Id) return index;
            return -1;
        }

        /// <summary>设置选中 Option，并只在 ID 发生变化时发送选择事件。</summary>
        /// <param name="option">新的选中 Option。</param>
        /// <returns>选中项发生变化时返回 true。</returns>
        private bool SetSelectedOption(InteractionOption option)
        {
            if (SelectedOption != null && option != null && SelectedOption.Id == option.Id) return false;
            if (SelectedOption == null && option == null) return false;

            SelectedOption = option;
            SelectionChanged?.Invoke(SelectedOption);
            return true;
        }

        #endregion

        #region Option 刷新

        /// <summary>从 Provider 重建候选 Option，并执行视野、遮挡、最大距离和业务筛选。</summary>
        private void RefreshOptions()
        {
            IReadOnlyList<IInteractable> providers = detector.Providers;
            InteractionOptionId previousSelectionId = SelectedOption != null
                ? SelectedOption.Id
                : default;
            bool hadPreviousSelection = SelectedOption != null;
            previousOptionIds.Clear();
            for (int index = 0; index < options.Count; index++) previousOptionIds.Add(options[index].Id);

            collectedOptions.Clear();
            optionIds.Clear();
            scoredOptions.Clear();
            InteractionQueryContext context = new(gameObject, transform, viewCamera);

            // 收集所有 Provider 的 Option，允许 Provider 自行筛选和生成。
            foreach (var provider in providers)
            {
                provider?.CollectInteractionOptions(in context, collectedOptions);
            }

            // 对收集到的 Option 执行硬筛选和评分，避免重复计算空间数据。
            foreach (var option in collectedOptions)
            {
                if (option == null || !optionIds.Add(option.Id)) continue;
                if (!TryScoreOption(option, out ScoredOption scoredOption)) continue;
                scoredOptions.Add(scoredOption);
            }

            scoredOptions.Sort(CompareScoredOptions);
            options.Clear();
            for (int index = 0; index < scoredOptions.Count; index++) options.Add(scoredOptions[index].Option);

            bool optionsChanged = !HaveSameOptionIds(options, previousOptionIds);
            InteractionOption nextSelection = null;
            if (options.Count > 0)
            {
                if (hadPreviousSelection)
                {
                    for (int index = 0; index < options.Count; index++)
                    {
                        if (options[index].Id != previousSelectionId) continue;
                        nextSelection = options[index];
                        break;
                    }
                }

                nextSelection ??= options[0];
            }

            bool selectionChanged = SelectedOption == null
                ? nextSelection != null
                : nextSelection == null || SelectedOption.Id != nextSelection.Id;
            SelectedOption = nextSelection;

            if (optionsChanged) OptionsChanged?.Invoke(options);
            if (selectionChanged) SelectionChanged?.Invoke(SelectedOption);
        }

        /// <summary>清空 Option 列表并对称发送列表和选择变化事件。</summary>
        private void ClearOptions()
        {
            bool hadOptions = options.Count > 0;
            bool hadSelection = SelectedOption != null;
            options.Clear();
            SelectedOption = null;
            if (hadOptions) OptionsChanged?.Invoke(options);
            if (hadSelection) SelectionChanged?.Invoke(null);
        }

        /// <summary>比较刷新前后 Option ID 顺序，避免仅因对象引用变化重复通知 UI。</summary>
        /// <param name="current">当前最终列表。</param>
        /// <param name="previousIds">上一帧最终列表的 Option ID 顺序。</param>
        /// <returns>列表实际保持不变时返回 true。</returns>
        private bool HaveSameOptionIds(IReadOnlyList<InteractionOption> current,
            IReadOnlyList<InteractionOptionId> previousIds)
        {
            if (current.Count != previousIds.Count) return false;
            for (int index = 0; index < current.Count; index++)
                if (current[index].Id != previousIds[index]) return false;
            return true;
        }

        #endregion

        #region 筛选与排序

        /// <summary>执行 Option 的硬筛选并计算排序所需的距离和镜头关注度。</summary>
        /// <param name="option">待筛选 Option。</param>
        /// <param name="scoredOption">通过筛选后带评分数据的 Option。</param>
        /// <returns>通过全部筛选时返回 true。</returns>
        private bool TryScoreOption(InteractionOption option, out ScoredOption scoredOption)
        {
            scoredOption = default;
            if (option.InteractionObject == null || option.InteractionOrigin == null) return false;

            Vector3 toOrigin = option.InteractionOrigin.position - transform.position;
            float distanceSqr = toOrigin.sqrMagnitude;
            if (option.MaxDistance > 0f && distanceSqr > option.MaxDistance * option.MaxDistance) return false;
            if (!option.CanExecute(gameObject)) return false;
            if (!IsVisible(option)) return false;

            float focusScore = 0f;
            if (viewCamera != null)
            {
                Vector3 cameraDirection = option.InteractionOrigin.position - viewCamera.transform.position;
                focusScore = cameraDirection.sqrMagnitude <= Mathf.Epsilon
                    ? 1f
                    : Vector3.Dot(viewCamera.transform.forward, cameraDirection.normalized);
            }

            scoredOption = new ScoredOption(option, distanceSqr, focusScore);
            return true;
        }

        /// <summary>判断 Option 是否位于摄像机 Viewport 内且未被其他实体遮挡。</summary>
        /// <param name="option">待判断 Option。</param>
        /// <returns>可见时返回 true。</returns>
        private bool IsVisible(InteractionOption option)
        {
            if (viewCamera == null) return true;
            Vector3 viewport = viewCamera.WorldToViewportPoint(option.InteractionOrigin.position);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f) return false;
            if (occlusionMask.value == 0) return true;

            Vector3 direction = option.InteractionOrigin.position - viewCamera.transform.position;
            float distance = direction.magnitude;
            if (distance <= Mathf.Epsilon) return true;

            int hitCount;
            do
            {
                hitCount = Physics.RaycastNonAlloc(viewCamera.transform.position, direction / distance,
                    occlusionHits, distance, occlusionMask, QueryTriggerInteraction.Ignore);
                if (hitCount < occlusionHits.Length) break;
                Array.Resize(ref occlusionHits, occlusionHits.Length * 2);
            } while (true);

            float closestDistance = float.PositiveInfinity;
            Collider closestCollider = null;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = occlusionHits[index];
                if (hit.collider == null || IsPlayerCollider(hit.collider)) continue;
                if (hit.distance >= closestDistance) continue;
                closestDistance = hit.distance;
                closestCollider = hit.collider;
            }

            return closestCollider == null || IsTargetCollider(closestCollider, option.InteractionObject.transform);
        }

        /// <summary>判断 Collider 是否属于玩家自身，避免第三人称摄像机先命中玩家。</summary>
        /// <param name="collider">待判断 Collider。</param>
        /// <returns>属于玩家层级时返回 true。</returns>
        private bool IsPlayerCollider(Collider collider) =>
            collider.transform == transform || collider.transform.IsChildOf(transform);

        /// <summary>判断 Collider 是否属于目标对象或其子层级。</summary>
        /// <param name="collider">射线命中的 Collider。</param>
        /// <param name="target">Option 的目标对象 Transform。</param>
        /// <returns>属于目标层级时返回 true。</returns>
        private static bool IsTargetCollider(Collider collider, Transform target) =>
            collider.transform == target || collider.transform.IsChildOf(target);

        /// <summary>按计划定义的优先级、关注度、距离和 ID 执行稳定排序。</summary>
        /// <param name="left">左侧评分项。</param>
        /// <param name="right">右侧评分项。</param>
        /// <returns>排序比较结果。</returns>
        private static int CompareScoredOptions(ScoredOption left, ScoredOption right)
        {
            int priorityComparison = right.Option.Priority.CompareTo(left.Option.Priority);
            if (priorityComparison != 0) return priorityComparison;
            int focusComparison = right.FocusScore.CompareTo(left.FocusScore);
            if (focusComparison != 0) return focusComparison;
            int distanceComparison = left.DistanceSqr.CompareTo(right.DistanceSqr);
            return distanceComparison != 0
                ? distanceComparison
                : left.Option.Id.CompareTo(right.Option.Id);
        }

        #endregion

        #region Intent 消费

        /// <summary>按导航优先、执行其次的顺序消费当前帧交互 Intent。</summary>
        private void ConsumeInputIntents()
        {
            if (stateBlackboard == null) return;
            ConsumeNavigationIntent(GameplayTags.Tag_Intent_Interaction_Previous, SelectPrevious);
            ConsumeNavigationIntent(GameplayTags.Tag_Intent_Interaction_Next, SelectNext);
            if (stateBlackboard.HasIntent(GameplayTags.Tag_Intent_Interaction_Execute) && SubmitSelected())
                stateBlackboard.TryConfirmIntentConsumed(GameplayTags.Tag_Intent_Interaction_Execute);
        }

        /// <summary>执行一个导航 Intent，并只在选择实际变化时确认输入消费。</summary>
        /// <param name="intentTag">导航 Intent 标签。</param>
        /// <param name="select">导航操作。</param>
        private void ConsumeNavigationIntent(WS_Modules.GAS.TAG.GameplayTag intentTag, Func<bool> select)
        {
            if (stateBlackboard.HasIntent(intentTag) && select()) stateBlackboard.TryConfirmIntentConsumed(intentTag);
        }

        #endregion

        #region 嵌套类型

        /// <summary>缓存 Option 在当前帧的排序评分，避免比较器重复计算空间数据。</summary>
        private readonly struct ScoredOption
        {
            /// <summary>获取交互 Option。</summary>
            public InteractionOption Option { get; }

            /// <summary>获取玩家到交互中心的平方距离。</summary>
            public float DistanceSqr { get; }

            /// <summary>获取镜头前方向与目标方向的点积。</summary>
            public float FocusScore { get; }

            /// <summary>创建一条 Option 评分缓存。</summary>
            /// <param name="option">交互 Option。</param>
            /// <param name="distanceSqr">平方距离。</param>
            /// <param name="focusScore">镜头关注度。</param>
            public ScoredOption(InteractionOption option, float distanceSqr, float focusScore)
            {
                Option = option;
                DistanceSqr = distanceSqr;
                FocusScore = focusScore;
            }
        }

        #endregion
    }
}
