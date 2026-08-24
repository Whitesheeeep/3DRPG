using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.Utilities;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 以可编辑 PhysicsShapeData 为范围周期执行体积检测，并输出去重后的 Provider 集合。
    /// </summary>
    [DefaultExecutionOrder(-750)]
    [DisallowMultipleComponent]
    public sealed class InteractionDetector : MonoBehaviour
    {
        #region 序列化配置与状态

        // 依赖 WSFrame.Utilities 的通用形状数据，Inspector 和 Scene Handle 共同编辑这份配置。
        [SerializeField, LabelText("检测形状")]
        private PhysicsShapeData detectionShape = new();
        [SerializeField, MinValue(0.01f), LabelText("扫描间隔 s")] private float scanInterval = 0.1f;
        [SerializeField, MinValue(1), LabelText("初始化缓冲区大小")] private int initialBufferSize = 64;
        [SerializeField] private LayerMask detectionMask = ~0;
        [SerializeField] private bool startDetect = true;

        // Provider 状态使用稳定列表对外暴露，并用双 Set 对比本次扫描与上一轮快照。
        private readonly List<IInteractable> providers = new();
        private readonly List<IInteractable> providerBuffer = new();
        private readonly HashSet<IInteractable> providerSet = new();
        private readonly HashSet<IInteractable> nextProviderSet = new();
        private Collider[] overlapBuffer;
        private float scanElapsed;

        #endregion

        #region 事件与属性

        /// <summary>Provider 集合发生变化时触发。</summary>
        public event Action<IReadOnlyList<IInteractable>> ProvidersChanged;

        /// <summary>获取最近一次扫描得到的 Provider 只读视图。</summary>
        public IReadOnlyList<IInteractable> Providers => providers;

        /// <summary>获取当前用于物理查询和可视化的检测形状。</summary>
        public PhysicsShapeData DetectionShape => detectionShape;

        /// <summary>获取当前检测器是否正在运行。</summary>
        public bool IsDetecting => startDetect;

        #endregion

        #region Unity 生命周期

        /// <summary>校验可编辑形状并初始化 NonAlloc 查询缓冲区。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            overlapBuffer = new Collider[initialBufferSize];
        }

        /// <summary>按不受暂停影响的真实时间周期刷新物理候选。</summary>
        private void Update()
        {
            if (!startDetect) return;
            scanElapsed += Time.unscaledDeltaTime;
            if (scanElapsed < scanInterval) return;

            scanElapsed = 0f;
            ScanNow();
        }

        /// <summary>绘制当前检测形状，便于在未选中玩家时持续观察交互区域。</summary>
        private void OnDrawGizmos()
        {
            // PhysicsShapeData 自己控制是否绘制；宿主只负责提供局部坐标所属的 Transform。
            detectionShape?.OnDrawGizmos(transform);
        }

        #endregion

        #region 检测控制

        /// <summary>开启检测并立即执行一次扫描。</summary>
        public void StartDetect()
        {
            startDetect = true;
            scanElapsed = 0f;
            ScanNow();
        }

        /// <summary>暂停检测并清除当前 Provider 集合。</summary>
        public void PauseDetect()
        {
            startDetect = false;
            scanElapsed = 0f;
            ClearProviders();
        }

        /// <summary>立即执行一次形状查询，供测试和恢复检测使用。</summary>
        public void ScanNow()
        {
            int overlapCount = OverlapShape();
            nextProviderSet.Clear();

            for (int index = 0; index < overlapCount; index++)
            {
                Collider collider = overlapBuffer[index];
                if (collider == null || IsPlayerHierarchy(collider.transform)) continue;

                // 直接查询接口并复用列表，避免为每个 Collider 分配组件数组和遍历无关脚本。
                providerBuffer.Clear();
                collider.GetComponentsInParent(true, providerBuffer);
                for (int providerIndex = 0; providerIndex < providerBuffer.Count; providerIndex++)
                    nextProviderSet.Add(providerBuffer[providerIndex]);
            }

            if (providerSet.SetEquals(nextProviderSet)) return;

            providerSet.Clear();
            providerSet.UnionWith(nextProviderSet);
            providers.Clear();
            providers.AddRange(providerSet);
            // 检测到数量
            Debug.Log($"InteractionDetector '{name}' 扫描到 {providers.Count} 个 Provider。");
            ProvidersChanged?.Invoke(providers);
        }

        #endregion

        #region 形状查询

        /// <summary>使用配置形状执行 NonAlloc 查询，并在结果满时扩容重查。</summary>
        /// <returns>本次查询命中的 Collider 数量。</returns>
        private int OverlapShape()
        {
            int count;
            do
            {
                count = PhysicsUtility.OverlapNonAlloc(transform, detectionShape, overlapBuffer,
                    detectionMask, QueryTriggerInteraction.Collide);
                if (count < overlapBuffer.Length) return count;

                // 缓冲区满时扩容并重查，避免把合法 Provider 静默截断。
                Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
            } while (true);
        }

        #endregion

        #region 状态与校验

        /// <summary>判断 Transform 是否属于玩家自身层级，避免把玩家 Collider 当作交互候选。</summary>
        /// <param name="candidate">待判断的 Collider 或组件 Transform。</param>
        /// <returns>属于玩家自身或子层级时返回 true。</returns>
        private bool IsPlayerHierarchy(Transform candidate) =>
            candidate == transform || candidate.IsChildOf(transform);

        /// <summary>清除扫描结果并在状态确实发生变化时通知订阅者。</summary>
        private void ClearProviders()
        {
            if (providers.Count == 0 && providerSet.Count == 0) return;
            providerSet.Clear();
            providers.Clear();
            ProvidersChanged?.Invoke(providers);
        }

        /// <summary>验证序列化配置的形状类型、尺寸、扫描间隔和缓冲容量。</summary>
        private void ValidateConfiguration()
        {
            if (detectionShape == null)
                throw new InvalidOperationException($"InteractionDetector '{name}' 缺少检测形状。");
            if (detectionShape.Type is PhysicsShapeType.None or PhysicsShapeType.Ray)
                throw new InvalidOperationException(
                    $"InteractionDetector '{name}' 只支持 Box、Sphere、Capsule 或 Sector 检测形状。");
            if (!IsShapeConfigurationValid(detectionShape))
                throw new InvalidOperationException($"InteractionDetector '{name}' 的检测形状尺寸不合法。");
            if (float.IsNaN(scanInterval) || float.IsInfinity(scanInterval) || scanInterval <= 0f)
                throw new InvalidOperationException("InteractionDetector scanInterval 必须是有限正数。");
            if (initialBufferSize <= 0)
                throw new InvalidOperationException("InteractionDetector initialBufferSize 必须为正数。");
        }

        /// <summary>检查通用形状数据的尺寸是否满足对应 Physics 查询契约。</summary>
        /// <param name="shape">待检查的检测形状。</param>
        /// <returns>形状可用于体积查询时返回 true。</returns>
        private static bool IsShapeConfigurationValid(PhysicsShapeData shape)
        {
            return shape.Type switch
            {
                PhysicsShapeType.Box => IsFinitePositive(shape.Size.x) &&
                    IsFinitePositive(shape.Size.y) && IsFinitePositive(shape.Size.z),
                PhysicsShapeType.Sphere => IsFinitePositive(shape.Radius),
                PhysicsShapeType.Capsule => IsFinitePositive(shape.Radius) &&
                    IsFinitePositive(shape.Height) && shape.Height >= shape.Radius * 2f,
                PhysicsShapeType.Sector => IsFinitePositive(shape.OuterRadius) &&
                    IsFiniteNonNegative(shape.InnerRadius) && shape.InnerRadius <= shape.OuterRadius &&
                    IsFinitePositive(shape.Height) && IsFinitePositive(shape.Angle) && shape.Angle <= 360f,
                _ => false
            };
        }

        /// <summary>判断数值是否为有限正数。</summary>
        private static bool IsFinitePositive(float value) => value > 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>判断数值是否为有限非负数。</summary>
        private static bool IsFiniteNonNegative(float value) => value >= 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
