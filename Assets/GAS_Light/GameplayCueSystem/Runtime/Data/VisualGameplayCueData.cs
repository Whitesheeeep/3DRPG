using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 保存需要对象池 GameObject 承载的视觉 Cue 资源与摆放规则。
    /// </summary>
    [CreateAssetMenu(fileName = "VisualGameplayCueData", menuName = "WSFrame/GAS/Gameplay Cue/Visual")]
    public sealed class VisualGameplayCueData : GameplayCueData
    {
        #region 视觉配置字段

        [SerializeField, Tooltip("在 Default Anchor 指定的 ASC Owner 中解析的 Marker；为空时使用 Owner 根节点。")]
        private MarkerKey markerKey;
        [SerializeField, WSAddressableKey, Tooltip("优先从对象池按此资源 Key 获取表现对象。")]
        private string addressableKey;
        [SerializeField, Tooltip("资源 Key 获取失败时使用的对象池 Prefab。")]
        private GameObject fallbackPrefab;
        [SerializeField, Tooltip("未指定显式挂点或世界坐标时使用的默认锚点。")]
        private GameplayCueAnchor defaultAnchor = GameplayCueAnchor.Target;
        [SerializeField, Tooltip("相对默认锚点的局部位置偏移。")]
        private Vector3 localPosition;
        [SerializeField, Tooltip("相对默认锚点的局部欧拉角偏移。")]
        private Vector3 localEulerAngles;
        [SerializeField, Tooltip("启用后将表现对象挂在解析出的默认锚点下。")]
        private bool followAnchor = true;

        #endregion

        #region 属性

        /// <summary>获取视觉对象需要解析的 Marker。</summary>
        public MarkerKey MarkerKey => markerKey;
        /// <summary>获取视觉对象的 Addressables 或资源池 Key。</summary>
        public string AddressableKey => addressableKey;
        /// <summary>获取 Key 加载失败时使用的对象池 Prefab。</summary>
        public GameObject FallbackPrefab => fallbackPrefab;
        /// <summary>获取视觉对象默认使用的 Source、Target 或 World 锚点。</summary>
        public GameplayCueAnchor DefaultAnchor => defaultAnchor;
        /// <summary>获取视觉对象相对锚点的位置偏移。</summary>
        public Vector3 LocalPosition => localPosition;
        /// <summary>获取视觉对象相对锚点的旋转偏移。</summary>
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
        /// <summary>获取视觉对象是否跟随默认锚点。</summary>
        public bool FollowAnchor => followAnchor;

        #endregion

#if UNITY_EDITOR
        #region 编辑器校验

        /// <summary>检查视觉资产的资源入口及 Prefab 回收契约。</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            GameObject prefab = null;
            try
            {
                prefab = fallbackPrefab;
                if (prefab != null)
                {
                    if (!prefab.TryGetComponent<GameplayCueBehaviour>(out _))
                        Debug.LogError($"VisualGameplayCueData '{name}' 的 Prefab 缺少 GameplayCueBehaviour。", prefab);
                    IGameObjectPoolable poolable = prefab.GetComponent<IGameObjectPoolable>();
                    if (poolable == null)
                        Debug.LogError($"VisualGameplayCueData '{name}' 的 Prefab 未实现 IGameObjectPoolable。", prefab);
                    else if (string.IsNullOrWhiteSpace(poolable.Key))
                        Debug.LogError($"VisualGameplayCueData '{name}' 的 Prefab Pool Key 不能为空。", prefab);
                    else if (!string.IsNullOrWhiteSpace(addressableKey) && poolable.Key != addressableKey)
                        Debug.LogError($"VisualGameplayCueData '{name}' 的 Prefab Pool Key 与 Addressable Key 不一致。", prefab);
                }
            }
            catch (MissingReferenceException)
            {
                Debug.LogError($"VisualGameplayCueData '{name}' 的 Prefab 引用失效，请重新指定或清空。", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(addressableKey) && prefab == null)
                Debug.LogError($"VisualGameplayCueData '{name}' 必须配置 Addressable Key 或 Prefab。", this);
        }

        #endregion
#endif
    }
}
