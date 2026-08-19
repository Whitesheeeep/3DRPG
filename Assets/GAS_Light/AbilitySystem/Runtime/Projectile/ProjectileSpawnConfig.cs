using System;
using RPG.Markers;
using UnityEngine;
using WS_Modules;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存 Projectile GA 与未来 SkillConfig 共用的发射、扇形和运动参数。</summary>
    [Serializable]
    public sealed class ProjectileSpawnConfig
    {
        #region 字段

        [SerializeField, WSAddressableKey, Tooltip("优先从对象池获取投射物的资源 Key。")]
        private string addressableKey;
        [SerializeField, Tooltip("Addressable Key 获取失败时使用的投射物 Prefab。")]
        private GameObject fallbackPrefab;
        [SerializeField, Tooltip("可选 Source 挂点；无法解析时回退 Source Transform。")]
        private MarkerKey markerKey;
        [SerializeField, Tooltip("相对生成挂点的局部位置偏移。")]
        private Vector3 localPosition;
        [SerializeField, Tooltip("相对生成挂点的局部欧拉角偏移。")]
        private Vector3 localEulerAngles;
        [SerializeField, Range(0f, 360f), Tooltip("全部投射物覆盖的总扇形角。")]
        private float spreadAngle;
        [SerializeField, Min(1), Tooltip("本次同步发射的投射物数量。")]
        private int projectileCount = 1;
        [SerializeField, Min(0f), Tooltip("投射物沿发射方向移动的速度。")]
        private float speed = 10f;
        [SerializeField, Min(0.01f), Tooltip("未命中有效 ASC 时的最长存活秒数。")]
        private float lifetime = 3f;
        [SerializeField, Tooltip("允许投射物结算的目标 ASC 根节点 Layer。")]
        private LayerMask targetLayerMask = ~0;

        #endregion

        #region 属性

        /// <summary>获取对象池资源 Key。</summary>
        public string AddressableKey => addressableKey;
        /// <summary>获取对象池资源失败时使用的 Prefab。</summary>
        public GameObject FallbackPrefab => fallbackPrefab;
        /// <summary>获取 Source 上用于发射的可选 Marker。</summary>
        public MarkerKey MarkerKey => markerKey;
        /// <summary>获取相对发射挂点的局部位置。</summary>
        public Vector3 LocalPosition => localPosition;
        /// <summary>获取相对发射挂点的局部旋转。</summary>
        public Vector3 LocalEulerAngles => localEulerAngles;
        /// <summary>获取全部投射物覆盖的总扇形角。</summary>
        public float SpreadAngle => spreadAngle;
        /// <summary>获取本次发射数量。</summary>
        public int ProjectileCount => projectileCount;
        /// <summary>获取投射物速度。</summary>
        public float Speed => speed;
        /// <summary>获取投射物最长存活时间。</summary>
        public float Lifetime => lifetime;
        /// <summary>获取允许投射物结算的目标 ASC 根节点 LayerMask。</summary>
        public LayerMask TargetLayerMask => targetLayerMask;
        /// <summary>获取当前配置是否满足 Projectile 公共资源与运动参数契约。</summary>
        internal bool IsValid =>
            (!string.IsNullOrWhiteSpace(addressableKey) || fallbackPrefab != null) &&
            projectileCount >= 1 && IsFinite(spreadAngle) && spreadAngle >= 0f && spreadAngle <= 360f &&
            speed >= 0f && IsFinite(speed) &&
            lifetime > 0f && IsFinite(lifetime);

        #endregion

        #region 内部辅助

        /// <summary>判断数值是否不是 NaN 或 Infinity。</summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>数值有限时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
