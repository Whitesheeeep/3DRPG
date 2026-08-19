using RPG.Markers;
using UnityEngine;
using WS_Modules;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>统一创建池化投射物的同步 Ability；Prefab 只决定投射物外观与碰撞形状。</summary>
    [CreateAssetMenu(fileName = "ProjectileGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Projectile")]
    public sealed class ProjectileGameplayAbilityData : SynchronousGameplayAbilityData
    {
        #region 字段与属性

        [SerializeField, Tooltip("投射物生成、扇形和运动参数。")]
        private ProjectileSpawnConfig spawnConfig = new();

        /// <summary>获取该 Ability 使用的通用投射物 Spawn 配置。</summary>
        public ProjectileSpawnConfig SpawnConfig => spawnConfig;
        /// <summary>获取所有投射物覆盖的总扇形角。</summary>
        public float SpreadAngle => spawnConfig != null ? spawnConfig.SpreadAngle : 0f;
        /// <summary>获取本次同步发射的投射物数量。</summary>
        public int ProjectileCount => spawnConfig != null ? spawnConfig.ProjectileCount : 0;
        /// <summary>获取对象池资源 Key。</summary>
        public string AddressableKey => spawnConfig?.AddressableKey;
        /// <summary>获取对象池资源失败时使用的 Prefab。</summary>
        public GameObject FallbackPrefab => spawnConfig?.FallbackPrefab;
        /// <summary>获取 Source 上用于发射的可选 Marker。</summary>
        public MarkerKey MarkerKey => spawnConfig?.MarkerKey;
        /// <summary>获取相对发射挂点的局部位置。</summary>
        public Vector3 LocalPosition => spawnConfig != null ? spawnConfig.LocalPosition : Vector3.zero;
        /// <summary>获取相对发射挂点的局部旋转。</summary>
        public Vector3 LocalEulerAngles => spawnConfig != null ? spawnConfig.LocalEulerAngles : Vector3.zero;
        /// <summary>获取投射物速度。</summary>
        public float Speed => spawnConfig != null ? spawnConfig.Speed : 0f;
        /// <summary>获取投射物最长存活时间。</summary>
        public float Lifetime => spawnConfig != null ? spawnConfig.Lifetime : 0f;

        /// <summary>投射物生成后独立存活，因此允许连续激活并生成多个实例。</summary>
        public override GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.AllowMultiple;

        /// <summary>校验同步 Projectile 的资源、数量与运动参数契约。</summary>
        internal override bool IsRuntimeConfigurationValid =>
            base.IsRuntimeConfigurationValid && spawnConfig != null && spawnConfig.IsValid;

        #endregion

        #region 同步执行

        /// <summary>立即生成全部投射物并结束同步 GA。</summary>
        /// <param name="runtime">本次同步 Ability 的运行快照。</param>
        protected override void Execute(SynchronousGameplayAbilityRuntime runtime)
        {
            Transform origin = ResolveSpawnTransform(runtime);
            ProjectileSpawnService.SpawnBatch(
                origin,
                spawnConfig,
                runtime.SourceASC,
                runtime.Level,
                runtime.SetByCaller,
                Effects,
                CueTags,
                runtime,
                this);
        }

        /// <summary>解析 Source Marker；Marker 缺失时回退 Source Transform。</summary>
        /// <param name="runtime">提供 Source ASC 的本次运行快照。</param>
        /// <returns>投射物 Pose 的参考 Transform。</returns>
        private Transform ResolveSpawnTransform(SynchronousGameplayAbilityRuntime runtime)
        {
            Transform sourceTransform = runtime.SourceOwner.RootTransform;
            if (spawnConfig.MarkerKey == null) return sourceTransform;

            IMarkerProvider provider = runtime.SourceOwner.MarkerProvider;
            if (provider != null && provider.TryGetMarker(spawnConfig.MarkerKey, out Transform marker))
                return marker;

            Debug.LogWarning(
                $"Projectile Ability '{name}' 无法在 Source '{runtime.SourceASC.name}' 解析 Marker " +
                $"'{spawnConfig.MarkerKey.name}'，将回退 Source Transform。",
                runtime.SourceASC);
            return sourceTransform;
        }

        #endregion
    }
}
