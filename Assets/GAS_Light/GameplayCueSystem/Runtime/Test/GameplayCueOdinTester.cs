#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using RPG.Markers;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 使用场景中提供的 Source 与 Target Actor，验证 GameplayCue 的挂点、世界位置、跟随和回收行为。
    /// </summary>
    public sealed class GameplayCueOdinTester : MonoBehaviour
    {
        #region 测试输入

        [SerializeField, AssetsOnly, Required]
        private GameplayCueDatabase database;

        [SerializeField, Required]
        private GameObject sourceActor;

        [SerializeField, Required]
        private GameObject targetActor;

        [SerializeField, AssetsOnly, Required]
        private GameplayCueData sourceCueData;

        [SerializeField, AssetsOnly, Required]
        private GameplayCueData worldCueData;

        [SerializeField, AssetsOnly, Required]
        private GameplayCueData targetCueData;

        [SerializeField, AssetsOnly, Required]
        private GameplayCueData followCueData;

        [SerializeField, Min(0.1f)]
        private float visualizationDuration = 5f;

        [SerializeField]
        private Vector3 followProbeOffset = Vector3.right;

        [SerializeField, Min(0.1f)]
        private float followProbeDuration = 1f;

        #endregion

        #region 运行状态

        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent target;
        private readonly List<GameplayCueRuntime> visualizationRuntimes = new();
        private Coroutine visualizationCoroutine;
        private Transform followedActor;
        private Vector3 followedActorPosition;
        private Quaternion followedActorRotation;
        private bool hasFollowSnapshot;

        #endregion

        #region Odin 操作

        /// <summary>
        /// 使用 Source CueData 执行一次性 Cue，验证表现对象可以创建并自动回收到对象池。
        /// </summary>
        [Button("测试 Execute Cue", ButtonSizes.Medium)]
        public void TestExecuteCue()
        {
            if (!TryPrepare()) return;
            if (!TryValidateCue("Execute", sourceCueData, GameplayCueAnchor.Source, false)) return;

            target.PublishGameplayCue(new GameplayCueRequest(
                sourceCueData.CueTag,
                GameplayCueEventType.Execute,
                source,
                target));

            Debug.Log("[CueTest][PASS] Execute 请求已发布；一次性 Cue 应由表现行为主动 Release。", this);
        }

        /// <summary>
        /// 使用 Source CueData 创建两次持续表现并逐个移除，验证 Active 与 Remove 生命周期。
        /// </summary>
        [Button("测试 Active/Remove Cue", ButtonSizes.Medium)]
        public void TestActiveCue()
        {
            if (!TryPrepare()) return;
            if (!TryValidateCue("Active", sourceCueData, GameplayCueAnchor.Source, false)) return;

            int beforeCount = target.Cues.ActiveCues.Count;
            target.PublishGameplayCue(new GameplayCueRequest(
                sourceCueData.CueTag,
                GameplayCueEventType.Active,
                source,
                target));
            GameplayCueRuntime first = FindNewActiveCue(beforeCount, sourceCueData);

            beforeCount = target.Cues.ActiveCues.Count;
            target.PublishGameplayCue(new GameplayCueRequest(
                sourceCueData.CueTag,
                GameplayCueEventType.Active,
                source,
                target));
            GameplayCueRuntime second = FindNewActiveCue(beforeCount, sourceCueData);

            int createdCount = 0;
            if (first != null) createdCount++;
            if (second != null && !ReferenceEquals(second, first)) createdCount++;
            bool firstRemoved = first != null && target.Cues.TryRemove(first);
            bool secondRemoved = second != null &&
                                 !ReferenceEquals(second, first) &&
                                 target.Cues.TryRemove(second);
            bool releasedExactlyOnce = firstRemoved && secondRemoved &&
                                       first.IsReleased && second.IsReleased;

            Debug.Log(releasedExactlyOnce
                    ? $"[CueTest][PASS] Active 创建数量={createdCount}，两个 Runtime 均完成单次移除和回收。"
                    : $"[CueTest][FAIL] Active 移除异常：Created={createdCount}, " +
                      $"FirstRemoved={firstRemoved}, SecondRemoved={secondRemoved}",
                this);
        }

        /// <summary>
        /// 显示挂载到 Source 的 Cue，并保持配置时间供 Scene 或 Game 视图观察。
        /// </summary>
        [Button("显示 Source Cue", ButtonSizes.Medium)]
        public void ShowSourceCue() => ShowSingleCue("Source", sourceCueData, GameplayCueAnchor.Source, false);

        /// <summary>
        /// 显示使用世界坐标的 Cue，并保持配置时间供 Scene 或 Game 视图观察。
        /// </summary>
        [Button("显示 World Cue", ButtonSizes.Medium)]
        public void ShowWorldCue() => ShowSingleCue("World", worldCueData, GameplayCueAnchor.World, false);

        /// <summary>
        /// 显示挂载到 Target 的 Cue，并保持配置时间供 Scene 或 Game 视图观察。
        /// </summary>
        [Button("显示 Target Cue", ButtonSizes.Medium)]
        public void ShowTargetCue() => ShowSingleCue("Target", targetCueData, GameplayCueAnchor.Target, false);

        /// <summary>
        /// 显示跟随挂点的 Cue，临时移动对应 Actor 并验证 Cue 是否同步移动。
        /// </summary>
        [Button("显示 Follow Cue", ButtonSizes.Medium)]
        public void ShowFollowCue() => ShowSingleCue("Follow", followCueData, null, true);

        /// <summary>
        /// 同时显示 Source、World、Target 和 Follow 四类 Cue，便于比较四种表现结果。
        /// </summary>
        [Button("执行四类 Cue 可视化", ButtonSizes.Large)]
        public void ShowAllCueVisualizations()
        {
            if (!TryPrepare()) return;
            if (!TryValidateCue("Source", sourceCueData, GameplayCueAnchor.Source, false) ||
                !TryValidateCue("World", worldCueData, GameplayCueAnchor.World, false) ||
                !TryValidateCue("Target", targetCueData, GameplayCueAnchor.Target, false) ||
                !TryValidateCue("Follow", followCueData, null, true))
            {
                return;
            }

            if (PublishVisualizationCue("Source", sourceCueData) == null ||
                PublishVisualizationCue("World", worldCueData) == null ||
                PublishVisualizationCue("Target", targetCueData) == null)
            {
                CleanupVisualization();
                return;
            }

            GameplayCueRuntime followRuntime = PublishVisualizationCue("Follow", followCueData);
            if (followRuntime == null)
            {
                CleanupVisualization();
                return;
            }

            StartVisualizationCoroutine(followRuntime);
        }

        /// <summary>
        /// 立即移除本测试创建的所有 Active Cue，并恢复 Follow 测试临时修改的 Actor 位置。
        /// </summary>
        [Button("清理 Cue 可视化")]
        public void CleanupCueVisualization() => CleanupVisualization();

        /// <summary>
        /// 清理当前测试器创建的 Cue，不影响外部 Actor 上已有的 GE、GA 或 Attribute 状态。
        /// </summary>
        [Button("清理 Cue 测试")]
        public void CleanupTest() => CleanupVisualization();

        #endregion

        #region 测试辅助

        // 准备真实 Actor 和 ASC；测试器只获取引用，不创建、销毁或重置外部对象。
        private bool TryPrepare()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[CueTest] Cue 可视化测试必须在 Play Mode 中执行。", this);
                return false;
            }

            CleanupVisualization();
            if (database == null || sourceActor == null || targetActor == null)
            {
                Debug.LogError("[CueTest] 请配置 CueDatabase、Source Actor 和 Target Actor。", this);
                return false;
            }

            source = sourceActor.GetComponent<GameplayAbilitySystemComponent>();
            target = targetActor.GetComponent<GameplayAbilitySystemComponent>();
            if (source == null || target == null)
            {
                Debug.LogError("[CueTest] Source Actor 和 Target Actor 根节点都必须挂载 GameplayAbilitySystemComponent。", this);
                source = null;
                target = null;
                return false;
            }

            if (ReferenceEquals(source, target))
            {
                Debug.LogError("[CueTest] Source Actor 和 Target Actor 必须是两个不同的 Actor。", this);
                source = null;
                target = null;
                return false;
            }

            if (source.Cues == null || target.Cues == null)
            {
                Debug.LogError("[CueTest] Source 或 Target ASC 的 Cue Controller 尚未初始化。", this);
                source = null;
                target = null;
                return false;
            }

            GameplayCueManager.Instance.Initialize(database);
            return true;
        }

        // 验证 Cue 配置、数据库注册关系和本次测试要求的 Anchor/Follow 约束。
        private bool TryValidateCue(
            string testName,
            GameplayCueData data,
            GameplayCueAnchor? expectedAnchor,
            bool requireFollow)
        {
            if (data == null)
            {
                Debug.LogError($"[CueTest][{testName}] 未配置 CueData。", this);
                return false;
            }

            if (!data.CueTag.IsValid || !GameplayCueManager.Instance.TryGetCue(data.CueTag, out GameplayCueData mapped) ||
                !ReferenceEquals(mapped, data))
            {
                Debug.LogError(
                    $"[CueTest][{testName}] CueData '{data.name}' 未按 CueTag 注册到当前 GameplayCueDatabase。",
                    data);
                return false;
            }

            if (expectedAnchor.HasValue && data.DefaultAnchor != expectedAnchor.Value)
            {
                Debug.LogError(
                    $"[CueTest][{testName}] CueData '{data.name}' 的 DefaultAnchor 应为 {expectedAnchor.Value}，实际为 {data.DefaultAnchor}。",
                    data);
                return false;
            }

            if (requireFollow && (data.DefaultAnchor == GameplayCueAnchor.World || !data.FollowAnchor))
            {
                Debug.LogError(
                    $"[CueTest][{testName}] Follow Cue 必须使用 Source/Target Anchor 且 FollowAnchor 为 true。",
                    data);
                return false;
            }

            if (requireFollow && followProbeOffset.sqrMagnitude < 0.000001f)
            {
                Debug.LogError("[CueTest][Follow] followProbeOffset 不能为零，否则无法验证跟随位置变化。", this);
                return false;
            }

            if (!requireFollow && expectedAnchor.HasValue && data.FollowAnchor)
            {
                Debug.LogError(
                    $"[CueTest][{testName}] 该静态挂点测试要求 CueData '{data.name}' 的 FollowAnchor 为 false。",
                    data);
                return false;
            }

            return true;
        }

        // 发布一次 Active 请求并记录新产生的 Runtime；请求统一由 Target ASC 接收。
        private GameplayCueRuntime PublishVisualizationCue(string testName, GameplayCueData data)
        {
            int beforeCount = target.Cues.ActiveCues.Count;
            target.PublishGameplayCue(new GameplayCueRequest(
                data.CueTag,
                GameplayCueEventType.Active,
                source,
                target));

            GameplayCueRuntime runtime = FindNewActiveCue(beforeCount, data);
            if (runtime == null || runtime.IsReleased || runtime.CueObject == null)
            {
                Debug.LogError($"[CueTest][{testName}][FAIL] Active Cue 未生成可视化 Runtime。", data);
                return null;
            }

            if (!ValidateInitialPlacement(testName, runtime, data))
            {
                runtime.Target.Cues.TryRemove(runtime);
                return null;
            }

            visualizationRuntimes.Add(runtime);
            Transform cueTransform = runtime.CueObject.transform;
            string parentName = cueTransform.parent == null ? "<根节点>" : cueTransform.parent.name;
            Debug.Log(
                $"[CueTest][{testName}][PASS] Object='{runtime.CueObject.name}' Parent='{parentName}' " +
                $"Position={cueTransform.position} Rotation={cueTransform.rotation.eulerAngles} " +
                $"保持 {Mathf.Max(0.1f, visualizationDuration):0.##} 秒。",
                runtime.CueObject);
            return runtime;
        }

        // 按 CueData 的 Anchor 和 FollowAnchor 语义校验初始父节点、位置与旋转。
        private bool ValidateInitialPlacement(string testName, GameplayCueRuntime runtime, GameplayCueData data)
        {
            Transform cueTransform = runtime.CueObject.transform;
            Transform anchor = ResolveExpectedAnchor(data);
            Vector3 expectedPosition = anchor == null
                ? data.LocalPosition
                : anchor.TransformPoint(data.LocalPosition);
            Quaternion expectedRotation = anchor == null
                ? data.LocalRotation
                : anchor.rotation * data.LocalRotation;

            bool parentCorrect = data.FollowAnchor
                ? anchor != null && cueTransform.parent == anchor
                : cueTransform.parent == null;
            bool positionCorrect = Vector3.Distance(cueTransform.position, expectedPosition) < 0.01f;
            bool rotationCorrect = Quaternion.Angle(cueTransform.rotation, expectedRotation) < 0.5f;
            if (parentCorrect && positionCorrect && rotationCorrect)
                return true;

            Debug.LogError(
                $"[CueTest][{testName}][FAIL] Cue 初始放置不符合配置：ParentCorrect={parentCorrect} " +
                $"PositionCorrect={positionCorrect} RotationCorrect={rotationCorrect}。",
                runtime.CueObject);
            return false;
        }

        // 使用与 GameplayCueCtrl 相同的 Source/Target 与 Marker 回退规则计算测试预期挂点。
        private Transform ResolveExpectedAnchor(GameplayCueData data)
        {
            if (data.DefaultAnchor == GameplayCueAnchor.World)
                return null;

            GameplayAbilitySystemComponent anchorAsc = data.DefaultAnchor == GameplayCueAnchor.Source
                ? source
                : target;
            if (anchorAsc == null || data.MarkerKey == null)
                return anchorAsc == null ? null : anchorAsc.transform;

            MarkerProvider provider = anchorAsc.GetComponent<MarkerProvider>();
            return provider != null && provider.TryGetMarker(data.MarkerKey, out Transform marker)
                ? marker
                : anchorAsc.transform;
        }

        // 查找本次发布后新增的 Active Runtime，避免依赖列表中其他外部 Cue 的顺序。
        private GameplayCueRuntime FindNewActiveCue(int beforeCount, GameplayCueData data)
        {
            IReadOnlyList<GameplayCueRuntime> activeCues = target.Cues.ActiveCues;
            for (int i = activeCues.Count - 1; i >= beforeCount && i >= 0; i--)
            {
                GameplayCueRuntime runtime = activeCues[i];
                if (!runtime.IsReleased && ReferenceEquals(runtime.CueData, data))
                    return runtime;
            }

            return null;
        }

        // 显示单个 Anchor 测试，并按需启动 Follow 验证和统一倒计时回收。
        private void ShowSingleCue(
            string testName,
            GameplayCueData data,
            GameplayCueAnchor? expectedAnchor,
            bool verifyFollow)
        {
            if (!TryPrepare() || !TryValidateCue(testName, data, expectedAnchor, verifyFollow)) return;

            GameplayCueRuntime runtime = PublishVisualizationCue(testName, data);
            if (runtime == null)
            {
                CleanupVisualization();
                return;
            }

            StartVisualizationCoroutine(verifyFollow ? runtime : null);
        }

        // 启动统一生命周期协程；Follow 测试先临时移动 Anchor，再等待配置时间回收。
        private void StartVisualizationCoroutine(GameplayCueRuntime followRuntime)
        {
            if (visualizationCoroutine != null)
                StopCoroutine(visualizationCoroutine);
            visualizationCoroutine = StartCoroutine(ReleaseVisualizationAfterDelay(
                Mathf.Max(0.1f, visualizationDuration),
                followRuntime));
        }

        // 验证 FollowAnchor 的实际位置变化，并在测试结束后恢复外部 Actor 的 Transform。
        private IEnumerator VerifyFollowAnchor(GameplayCueRuntime runtime)
        {
            Transform anchorActor = followCueData.DefaultAnchor == GameplayCueAnchor.Source
                ? sourceActor.transform
                : targetActor.transform;
            Vector3 initialCuePosition = runtime.CueObject.transform.position;
            followedActor = anchorActor;
            followedActorPosition = anchorActor.position;
            followedActorRotation = anchorActor.rotation;
            hasFollowSnapshot = true;
            anchorActor.position += followProbeOffset;

            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, followProbeDuration));

            if (runtime.IsReleased || runtime.CueObject == null)
            {
                Debug.LogError("[CueTest][Follow][FAIL] Follow 验证期间 Cue Runtime 已被外部回收。", this);
                RestoreFollowActor();
                yield break;
            }

            bool followed = Vector3.Distance(
                runtime.CueObject.transform.position - initialCuePosition,
                followProbeOffset) < 0.01f;
            Debug.Log(
                followed
                    ? "[CueTest][Follow][PASS] Cue 已跟随 Anchor Actor 移动。"
                    : "[CueTest][Follow][FAIL] Cue 未按预期跟随 Anchor Actor 移动。",
                runtime.CueObject);
            RestoreFollowActor();
        }

        // 等待真实时间后移除本次创建的所有 Runtime，不影响外部 ASC 的其他 Cue。
        private IEnumerator ReleaseVisualizationAfterDelay(float duration, GameplayCueRuntime followRuntime)
        {
            if (followRuntime != null)
                yield return VerifyFollowAnchor(followRuntime);

            yield return new WaitForSecondsRealtime(duration);
            visualizationCoroutine = null;
            RestoreFollowActor();

            int removedCount = 0;
            for (int i = visualizationRuntimes.Count - 1; i >= 0; i--)
            {
                GameplayCueRuntime runtime = visualizationRuntimes[i];
                if (runtime != null && runtime.Target != null && runtime.Target.Cues.TryRemove(runtime))
                    removedCount++;
            }

            visualizationRuntimes.Clear();
            Debug.Log($"[CueTest][PASS] 可视化保持时间结束，已回收 {removedCount} 个本次测试创建的 Active Cue。", this);
        }

        // 停止协程、恢复 Follow Actor，并逐个移除本测试拥有的 Cue Runtime。
        private void CleanupVisualization()
        {
            if (visualizationCoroutine != null)
            {
                StopCoroutine(visualizationCoroutine);
                visualizationCoroutine = null;
            }

            RestoreFollowActor();
            for (int i = visualizationRuntimes.Count - 1; i >= 0; i--)
            {
                GameplayCueRuntime runtime = visualizationRuntimes[i];
                if (runtime != null && runtime.Target != null)
                    runtime.Target.Cues.TryRemove(runtime);
            }

            visualizationRuntimes.Clear();
            source = null;
            target = null;
        }

        // 恢复 Follow 测试临时移动的 Actor，保证测试器不改变场景角色初始状态。
        private void RestoreFollowActor()
        {
            if (!hasFollowSnapshot || followedActor == null)
            {
                hasFollowSnapshot = false;
                followedActor = null;
                return;
            }

            followedActor.SetPositionAndRotation(followedActorPosition, followedActorRotation);
            hasFollowSnapshot = false;
            followedActor = null;
        }

        // 组件销毁时仅回收测试器创建的 Cue，并恢复临时 Transform 变更。
        private void OnDestroy() => CleanupVisualization();

        #endregion
    }
}
#endif
