using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 以玩家 CharacterController 为中心周期执行胶囊范围检测，并输出去重后的 Provider 集合。
    /// </summary>
    [DefaultExecutionOrder(-750)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class InteractionDetector : MonoBehaviour
    {
        #region 序列化配置与状态

        [SerializeField, Min(0f)] private float detectionRange = 8f;
        [SerializeField, Min(0.01f)] private float scanInterval = 0.1f;
        [SerializeField, Min(1)] private int initialBufferSize = 64;
        [SerializeField] private LayerMask detectionMask = ~0;
        [SerializeField] private bool startDetect = true;
        // 依赖的实际是 cc 的 center、radius、height，若 cc 为空则会在 Awake 抛异常。
        // 主要逻辑在：OverlapCapsuleWithExpansion
        // TODO: 未来考虑使用 CapsuleCollider 代替 CharacterController，避免依赖 cc，而不能复用于非玩家角色。
        [SerializeField] private CharacterController characterController;

        private readonly List<IInteractable> providers = new();
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

        /// <summary>获取玩家检测器的粗筛范围。</summary>
        public float DetectionRange => detectionRange;

        /// <summary>获取当前检测器是否正在运行。</summary>
        public bool IsDetecting => startDetect;

        #endregion

        #region Unity 生命周期

        /// <summary>解析同节点 CharacterController 并初始化 NonAlloc 查询缓冲区。</summary>
        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (characterController == null)
                throw new InvalidOperationException($"InteractionDetector '{name}' 缺少 CharacterController。");

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

        /// <summary>立即执行一次胶囊查询，供测试和恢复检测使用。</summary>
        public void ScanNow()
        {
            if (characterController == null) return;

            int overlapCount = OverlapCapsuleWithExpansion();
            nextProviderSet.Clear();

            for (int index = 0; index < overlapCount; index++)
            {
                Collider collider = overlapBuffer[index];
                if (collider == null || IsPlayerHierarchy(collider.transform)) continue;

                MonoBehaviour[] components = collider.GetComponentsInParent<MonoBehaviour>(true);
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    MonoBehaviour component = components[componentIndex];
                    if (component == null || IsPlayerHierarchy(component.transform)) continue;
                    if (component is IInteractable provider) nextProviderSet.Add(provider);
                }
            }

            if (providerSet.SetEquals(nextProviderSet)) return;

            providerSet.Clear();
            providerSet.UnionWith(nextProviderSet);
            providers.Clear();
            providers.AddRange(providerSet);
            ProvidersChanged?.Invoke(providers);
        }

        #endregion

        #region 胶囊查询

        /// <summary>使用 CharacterController 几何体外扩检测范围执行 NonAlloc 胶囊查询。</summary>
        /// <returns>本次查询命中的 Collider 数量。</returns>
        private int OverlapCapsuleWithExpansion()
        {
            Vector3 center = transform.TransformPoint(characterController.center);
            Vector3 up = transform.up;
            float baseRadius = Mathf.Max(0.01f, characterController.radius);
            float radius = baseRadius + detectionRange;
            float halfHeight = Mathf.Max(characterController.height * 0.5f + detectionRange, radius);
            Vector3 pointA = center - up * (halfHeight - radius);
            Vector3 pointB = center + up * (halfHeight - radius);

            int count;
            do
            {
                count = Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, overlapBuffer,
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

        /// <summary>验证序列化配置的有限范围和有效缓冲容量。</summary>
        private void ValidateConfiguration()
        {
            if (float.IsNaN(detectionRange) || float.IsInfinity(detectionRange) || detectionRange < 0f)
                throw new InvalidOperationException("InteractionDetector detectionRange 必须是有限非负数。");
            if (float.IsNaN(scanInterval) || float.IsInfinity(scanInterval) || scanInterval <= 0f)
                throw new InvalidOperationException("InteractionDetector scanInterval 必须是有限正数。");
            if (initialBufferSize <= 0) throw new InvalidOperationException("InteractionDetector initialBufferSize 必须大于 0。");
        }

        #endregion
    }
}
