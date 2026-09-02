using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Game.UI.Events;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using WS_Modules.CustomEventSystem;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 挂载在移动角色节点的交互编排组件，从父级玩家读取输入并维护 Option 列表与选择。
    /// 跨场景生命周期由所属 Player 管理，本组件不单独保留或移动 CharacterRoot。
    /// </summary>
    [DefaultExecutionOrder(-700)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractionDetector))]
    [InfoBox("挂载于移动的 CharacterRoot；依赖同节点 InteractionDetector，以及自身或父级的 PlayerController。" +
        "PlayerController 只用于提供稳定玩家对象（执行序 -800，交互组件 -700）；缺失时立即报错。" +
        "业务执行使用 PlayerController 所在对象，空间检测与距离计算使用当前角色节点。")]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        #region 序列化引用与状态

        [SerializeField, Tooltip("传给 Provider 查询上下文的摄像机，一般就是主摄像机。")]
        private Camera viewCamera;
        [SerializeField, Tooltip("保留视口与遮挡配置，当前版本不作为 Option 评分或硬筛选。")]
        private LayerMask occlusionMask = ~0;
        [SerializeField] private InteractionDetector detector;
        [SerializeField, FormerlySerializedAs("startDetect")]
        private bool startDetectOnEnable = true;

        private readonly List<InteractionOption> options = new();
        private readonly List<InteractionOption> collectedOptions = new();
        private readonly HashSet<InteractionOptionId> optionIds = new();

        private readonly List<InteractionOption> filteredOptions = new();
        private readonly List<InteractionOptionId> previousOptionIds = new();
        // GameUILock 来源按 SourceId 去重；每个独占流程只拥有自己的一份锁定引用。
        private readonly HashSet<string> gameUILockSources = new();
        private bool isDetecting;
        // 只在锁定从 0 变为 1 时记录，最后一个来源释放后按此状态决定是否恢复。
        private bool resumeDetectionAfterGameUIUnlock;
        private RaycastHit[] occlusionHits = new RaycastHit[16];
        // 依赖父级 PlayerController 的稳定玩家身份；角色节点只承担移动空间基准。
        private GameObject interactorObject;

        #endregion

        #region 事件与属性

        /// <summary>获取当前本地玩家的交互组件；只记录引用，不单独管理对象的跨场景保留。</summary>
        public static PlayerInteractor Instance { get; private set; }

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

        /// <summary>解析同节点检测器和父级玩家控制器，再发布可供窗口绑定的本地玩家实例。</summary>
        /// <exception cref="InvalidOperationException">存在重复交互实例，或玩家控制器未就绪。</exception>
        private void Awake()
        {
            // 重复配置不能销毁 CharacterRoot，否则会连带删除角色、碰撞器和队伍管理器。
            if (Instance != null && Instance != this)
                throw new InvalidOperationException("[PlayerInteractor] 本地玩家只能存在一个交互实例。");

            if (detector == null) detector = GetComponent<InteractionDetector>();
            if (viewCamera == null) viewCamera = Camera.main;

            PlayerController playerController = GetComponentInParent<PlayerController>(true);
            if (playerController == null)
                throw new InvalidOperationException(
                    $"[PlayerInteractor] '{name}' 的自身或父级缺少 PlayerController。");
            // 业务接收器位于稳定 Player 上；查询位置仍取本组件所在的移动 CharacterRoot。
            interactorObject = playerController.gameObject;
            EventSystem
                .Register_Type<GameUILockChangeRequestedEventArgs>(
                    typeof(GameUILockChangeRequestedEventArgs),
                    OnGameUILockChangeRequested)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            Instance = this;
            InstanceChanged?.Invoke(this);
        }

        /// <summary>当前实例随所属玩家销毁时清空静态引用，并通知窗口 Controller 解除模型绑定。</summary>
        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            InstanceChanged?.Invoke(null);
        }

        /// <summary>订阅检测结果并按组件配置启动范围检测。</summary>
        private void OnEnable()
        {
            // Awake 配置失败的实例未发布，不能继续扫描或订阅事件。
            if (Instance != this) return;
            if (detector != null) detector.ScanCompleted += OnScanCompleted;
            if (gameUILockSources.Count == 0 && startDetectOnEnable) StartDetect();
        }

        /// <summary>解绑检测事件、暂停检测并清理当前交互状态。</summary>
        private void OnDisable()
        {
            if (Instance != this) return;
            if (detector != null) detector.ScanCompleted -= OnScanCompleted;
            PauseDetect();
        }

        #endregion

        #region 检测控制

        /// <summary>开启交互检测并立即刷新 Option 列表。</summary>
        public void StartDetect()
        {
            if (gameUILockSources.Count != 0) return;
            isDetecting = true;
            detector.StartDetect();
        }

        /// <summary>暂停交互检测并清空 Option 与选择状态。</summary>
        public void PauseDetect()
        {
            isDetecting = false;
            if (detector != null && detector.IsDetecting) detector.PauseDetect();
            ClearOptions();
        }

        /// <summary>响应检测器扫描完成，重建包含动态业务状态的最终 Option 列表。</summary>
        private void OnScanCompleted()
        {
            // PauseDetect 后可能仍有同帧扫描回调，锁定期间不得重新暴露交互选项。
            if (gameUILockSources.Count != 0) return;
            RefreshOptions();
        }

        #endregion

        #region GameUILock 处理

        /// <summary>
        /// 按来源接收独占 Game UI 请求；第一个来源暂停检测，最后一个来源释放时恢复原状态。
        /// </summary>
        /// <param name="eventArgs">GameUILock 变更请求。</param>
        private void OnGameUILockChangeRequested(GameUILockChangeRequestedEventArgs eventArgs)
        {
            if (eventArgs.Operation == GameUILockOperation.Acquire)
            {
                if (!gameUILockSources.Add(eventArgs.SourceId)) return;
                if (gameUILockSources.Count != 1) return;

                // 只有从无锁到首个锁定时记录状态，避免后续来源覆盖恢复依据。
                resumeDetectionAfterGameUIUnlock = isDetecting;
                PauseDetect();
                return;
            }

            if (!gameUILockSources.Remove(eventArgs.SourceId) || gameUILockSources.Count != 0)
                return;

            bool shouldResume = resumeDetectionAfterGameUIUnlock;
            resumeDetectionAfterGameUIUnlock = false;
            // Disable 状态不主动启动检测；重新启用时由 OnEnable 按原有配置处理。
            if (shouldResume && isActiveAndEnabled) StartDetect();
        }

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
            return SelectedOption != null && SelectedOption.TryExecute(interactorObject);
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

        /// <summary>从 Provider 重建候选 Option，并按角色节点的最大距离与玩家业务状态筛选。</summary>
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
            filteredOptions.Clear();
            InteractionQueryContext context = new(interactorObject, transform, viewCamera);

            // 收集所有 Provider 的 Option，允许 Provider 自行筛选和生成。
            foreach (var provider in providers)
            {
                provider?.CollectInteractionOptions(in context, collectedOptions);
            }

            // 对收集到的 Option 执行动态硬筛选；排序不再依赖镜头关注度或距离评分。
            foreach (var option in collectedOptions)
            {
                if (option == null || !optionIds.Add(option.Id)) continue;
                if (!TryAcceptOption(option)) continue;
                filteredOptions.Add(option);
            }

            filteredOptions.Sort(CompareOptions);
            options.Clear();
            options.AddRange(filteredOptions);

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

        /// <summary>执行 Option 的动态硬筛选，不把距离或镜头关注度作为排序评分。</summary>
        /// <param name="option">待筛选 Option。</param>
        /// <returns>通过全部筛选时返回 true。</returns>
        private bool TryAcceptOption(InteractionOption option)
        {
            if (option.InteractionObject == null || option.InteractionOrigin == null) return false;

            Vector3 toOrigin = option.InteractionOrigin.position - transform.position;
            float distanceSqr = toOrigin.sqrMagnitude;
            if (option.MaxDistance > 0f && distanceSqr > option.MaxDistance * option.MaxDistance) return false;
            if (!option.CanExecute(interactorObject)) return false;
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

        /// <summary>按优先级降序和稳定 Option ID 升序执行确定性排序。</summary>
        /// <param name="left">左侧 Option。</param>
        /// <param name="right">右侧 Option。</param>
        /// <returns>排序比较结果。</returns>
        private static int CompareOptions(InteractionOption left, InteractionOption right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            if (priorityComparison != 0) return priorityComparison;
            return left.Id.CompareTo(right.Id);
        }

        #endregion

    }
}
