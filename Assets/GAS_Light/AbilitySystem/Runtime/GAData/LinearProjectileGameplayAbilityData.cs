using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>从 Source 前方生成对象池线性投射物，并让投射物独立完成命中结算。</summary>
    [CreateAssetMenu(fileName = "LinearProjectileGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Projectile/Linear")]
    public sealed class LinearProjectileGameplayAbilityData : ProjectileGameplayAbilityData
    {
        #region 字段与属性

        [SerializeField, WSAddressableKey, Tooltip("优先从 PoolManager 获取投射物的资源 Key。")]
        private string addressableKey;
        [SerializeField, Tooltip("Addressable Key 获取失败时使用的投射物预制体。")]
        private GameObject fallbackPrefab;
        [SerializeField, Tooltip("可选 Source 挂点；无法解析时回退 Source Transform。")]
        private MarkerKey spawnMarker;
        [SerializeField, Tooltip("相对生成挂点的局部位置偏移。")]
        private Vector3 localPosition;
        [SerializeField, Tooltip("相对生成挂点的局部欧拉角偏移。")]
        private Vector3 localEulerAngles;
        [SerializeField, Min(0f), Tooltip("投射物沿生成方向移动的速度。")]
        private float speed = 10f;
        [SerializeField, Min(0.01f), Tooltip("未命中有效 ASC 时的最长存活秒数。")]
        private float lifetime = 3f;

        /// <summary>获取优先使用的对象池资源 Key。</summary>
        public string AddressableKey => addressableKey;
        /// <summary>获取资源 Key 失败时使用的预制体。</summary>
        public GameObject FallbackPrefab => fallbackPrefab;
        /// <summary>获取 Source 上用于生成投射物的可选 Marker。</summary>
        public MarkerKey SpawnMarker => spawnMarker;
        /// <summary>获取相对生成挂点的局部位置偏移。</summary>
        public Vector3 LocalPosition => localPosition;
        /// <summary>获取相对生成挂点的局部欧拉角偏移。</summary>
        public Vector3 LocalEulerAngles => localEulerAngles;
        /// <summary>获取投射物移动速度。</summary>
        public float Speed => speed;
        /// <summary>获取投射物最长存活秒数。</summary>
        public float Lifetime => lifetime;

        // 资源引用属于作者边界，至少一种资源入口有效且运动参数有限时才能提交 Cost/Cooldown。
        internal override bool IsRuntimeConfigurationValid =>
            base.IsRuntimeConfigurationValid &&
            (!string.IsNullOrWhiteSpace(addressableKey) || fallbackPrefab != null) &&
            speed >= 0f && IsFinite(speed) &&
            lifetime > 0f && IsFinite(lifetime);

        #endregion

        #region 投射物生成

        /// <summary>从对象池获取投射物，设置 Source 挂点位置并注入本次激活快照。</summary>
        /// <param name="runtime">本次同步 Ability Runtime。</param>
        protected override void SpawnProjectile(SynchronousGameplayAbilityRuntime runtime)
        {
            GameObject projectile = GetProjectileObject();
            if (projectile == null)
            {
                Debug.LogError($"Linear Projectile Ability '{name}' 无法从 Addressable Key 或 Fallback Prefab 获取投射物。", this);
                return;
            }

            if (!projectile.TryGetComponent(out GameplayAbilityProjectileBehaviour behaviour))
            {
                Debug.LogError($"投射物 '{projectile.name}' 缺少 GameplayAbilityProjectileBehaviour。", projectile);
                PoolManager.Instance.Recycle(projectile);
                return;
            }

            Transform spawnTransform = ResolveSpawnTransform(runtime);
            Quaternion rotation = spawnTransform.rotation * Quaternion.Euler(localEulerAngles);
            Vector3 position = spawnTransform.TransformPoint(localPosition);

            // 生成 Pose 与运行快照一次性交给刚体 Behaviour，避免池化 Rigidbody 继续使用旧位置。
            behaviour.Initialize(
                runtime.Source,
                runtime.Level,
                runtime.SetByCaller,
                Effects,
                CueTags,
                runtime,
                position,
                rotation,
                rotation * Vector3.forward,
                speed,
                lifetime);
        }

        /// <summary>按资源 Key 优先、Prefab 回退的顺序从 PoolManager 获取实例。</summary>
        /// <returns>成功获取的投射物对象；两个入口均失败时返回 null。</returns>
        private GameObject GetProjectileObject()
        {
            GameObject projectile = null;
            if (!string.IsNullOrWhiteSpace(addressableKey))
                projectile = PoolManager.Instance.Get(addressableKey);
            if (projectile == null && fallbackPrefab != null)
                projectile = PoolManager.Instance.Get(fallbackPrefab);
            return projectile;
        }

        /// <summary>在 Source 根 MarkerProvider 查询挂点，失败时使用 Source Transform。</summary>
        /// <param name="runtime">提供 Source 的本次 Ability Runtime。</param>
        /// <returns>用于生成投射物的 Transform。</returns>
        private Transform ResolveSpawnTransform(SynchronousGameplayAbilityRuntime runtime)
        {
            Transform sourceTransform = runtime.Source.transform;
            if (spawnMarker == null) return sourceTransform;

            IMarkerProvider provider = runtime.Source.GetComponent<IMarkerProvider>();
            if (provider != null && provider.TryGetMarker(spawnMarker, out Transform marker))
                return marker;

            Debug.LogWarning(
                $"Linear Projectile Ability '{name}' 无法在 Source '{runtime.Source.name}' 解析 Marker '{spawnMarker.name}'，将回退 Source Transform。",
                runtime.Source);
            return sourceTransform;
        }

        /// <summary>判断投射物时间和速度配置是否为有限值。</summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>不是 NaN 或 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
