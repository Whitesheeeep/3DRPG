using UnityEngine;
using WS_Modules.Pooling;
using WS_Modules.GAS.TAG;
using RPG.Markers;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>保存一个可复用 GameplayCue 的标签、资源入口和默认摆放规则。</summary>
    [CreateAssetMenu(fileName = "GameplayCueData", menuName = "WSFrame/GAS/Gameplay Cue")]
    public sealed class GameplayCueData : ScriptableObject
    {
        #region 作者配置
        [SerializeField, Tooltip("用于 CueDatabase 映射的稳定 GameplayTag。")]
        private GameplayTag cueTag;
        [SerializeField, Tooltip("在 Default Anchor Mode 指定的 ASC 的 MarkerProvider 中查找；找不到时回退到该 ASC Transform。")]
        private MarkerKey markerKey;
        [SerializeField, WSAddressableKey, Tooltip("优先使用的对象池资源 Key，可以是 Addressable Key。")]
        private string addressableKey;
        [SerializeField, Tooltip("Addressable Key 加载失败时使用的 Prefab。")]
        private GameObject fallbackPrefab;
        [SerializeField, Tooltip("默认挂载对象模式：无显式挂点或世界位置时，决定使用 Source、Target 或 World。")]
        private GameplayCueAnchor defaultAnchor = GameplayCueAnchor.Target;
        [SerializeField, Tooltip("相对默认挂点的局部位置偏移。")]
        private Vector3 localPosition;
        [SerializeField, Tooltip("相对默认挂点的局部欧拉角偏移。")]
        private Vector3 localEulerAngles;
        [SerializeField, Tooltip("是否跟随 Default Anchor Mode 解析出的 Marker 或 ASC Transform。")]
        private bool followAnchor = true;
        #endregion

        #region 属性
        /// <summary>获取用于运行时查表的 CueTag。</summary>
        public GameplayTag CueTag => cueTag;
        /// <summary>获取默认挂点 Marker；为空时使用对应 ASC 的 Transform。</summary>
        public MarkerKey MarkerKey => markerKey;
        /// <summary>获取优先使用的 Addressable 或资源池 Key。</summary>
        public string AddressableKey => addressableKey;
        /// <summary>获取资源 Key 失败时使用的 Prefab。</summary>
        public GameObject FallbackPrefab => fallbackPrefab;
        /// <summary>获取默认挂载位置类型。</summary>
        public GameplayCueAnchor DefaultAnchor => defaultAnchor;
        /// <summary>获取默认局部位置偏移。</summary>
        public Vector3 LocalPosition => localPosition;
        /// <summary>获取默认局部旋转偏移。</summary>
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
        /// <summary>获取表现是否跟随默认挂点。</summary>
        public bool FollowAnchor => followAnchor;
        #endregion

#if UNITY_EDITOR
        // 预制体必须同时具备表现行为和可定位的对象池标识，避免运行时才发现无法回收。
        /// <summary>在资源校验边界检查 CueTag 和 Fallback Prefab 配置，失效引用只报告问题而不让 Unity 校验流程中断。</summary>
        private void OnValidate()
        {
            if (!cueTag.IsValid)
                Debug.LogError($"GameplayCueData '{name}' 的 CueTag 无效。", this);

            // Unity 允许托管字段继续保存已销毁对象的包装，Prefab 访问必须在一个 try 边界内完成。
            GameObject prefab = null;
            bool hasPrefab = false;
            try
            {
                prefab = fallbackPrefab;
                hasPrefab = prefab != null;
                if (hasPrefab && !prefab.TryGetComponent<GameplayCueBehaviour>(out _))
                    Debug.LogError($"GameplayCueData '{name}' 的 Fallback Prefab 缺少 GameplayCueBehaviour。", prefab);
                if (hasPrefab && prefab.TryGetComponent<PoolObjectIdentity>(out PoolObjectIdentity identity) &&
                    string.IsNullOrWhiteSpace(identity.PoolKey))
                    Debug.LogWarning($"GameplayCueData '{name}' 的 Fallback Prefab PoolObjectIdentity.PoolKey 为空，将使用预制体名称作为池 Key。", prefab);
            }
            catch (MissingReferenceException)
            {
                Debug.LogError($"GameplayCueData '{name}' 的 Fallback Prefab 引用已失效，请重新指定或清空该字段。", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(addressableKey) && !hasPrefab)
                Debug.LogError($"GameplayCueData '{name}' 必须配置 Addressable Key 或 Fallback Prefab。", this);
        }
#endif
    }
}
