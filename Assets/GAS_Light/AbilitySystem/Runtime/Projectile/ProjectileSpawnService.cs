using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>执行 Projectile 批量池化生成，供同步 GA 和未来 SkillConfig Task 共享。</summary>
    public static class ProjectileSpawnService
    {
        /// <summary>按配置立即生成整组投射物并注入统一 Ability 快照。</summary>
        /// <param name="origin">Marker 或 Source Transform 发射参考。</param>
        /// <param name="config">投射物 Spawn 配置。</param>
        /// <param name="source">本次投射物的 Source ASC。</param>
        /// <param name="level">Ability 等级快照。</param>
        /// <param name="setByCaller">Ability 动态值快照。</param>
        /// <param name="effects">投射物命中时应用的 Effects。</param>
        /// <param name="cueTags">投射物命中时发布的 CueTags。</param>
        /// <param name="abilityRuntime">产生投射物的 Ability Runtime。</param>
        /// <param name="logContext">资源失败时使用的日志上下文。</param>
        /// <returns>成功初始化的投射物数量。</returns>
        internal static int SpawnBatch(
            Transform origin,
            ProjectileSpawnConfig config,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            IReadOnlyList<GameplayEffectData> effects,
            IReadOnlyList<GameplayTag> cueTags,
            GameplayAbilityRuntime abilityRuntime,
            Object logContext)
        {
            int spawnedCount = 0;
            for (int index = 0; index < config.ProjectileCount; index++)
            {
                ProjectileSpawnPose pose = ProjectileSpawnUtility.CalculatePose(
                    origin,
                    config.LocalPosition,
                    config.LocalEulerAngles,
                    config.SpreadAngle,
                    config.ProjectileCount,
                    index);
                GameObject projectile = GetProjectileObject(config);
                if (projectile == null)
                {
                    Debug.LogError(
                        $"Projectile 无法从 Addressable Key 或 Fallback Prefab 获取第 {index + 1} 发。",
                        logContext);
                    continue;
                }

                if (!projectile.TryGetComponent(out GameplayAbilityProjectileBehaviour behaviour))
                {
                    Debug.LogError(
                        $"投射物 '{projectile.name}' 缺少 GameplayAbilityProjectileBehaviour。",
                        projectile);
                    PoolManager.Instance.Recycle(projectile);
                    continue;
                }

                // 位置、旋转和方向与 Ability 快照一起提交，避免池化实例继续使用上一轮 Pose。
                behaviour.Initialize(
                    source,
                    level,
                    setByCaller,
                    effects,
                    cueTags,
                    abilityRuntime,
                    pose.Position,
                    pose.Rotation,
                    pose.Direction,
                    config.Speed,
                    config.Lifetime,
                    config.TargetLayerMask);
                spawnedCount++;
            }
            return spawnedCount;
        }

        /// <summary>按资源 Key 优先、Prefab 回退的顺序获取一枚投射物。</summary>
        /// <param name="config">提供对象池资源入口的配置。</param>
        /// <returns>成功获取的池化实例；资源入口都失败时返回 null。</returns>
        private static GameObject GetProjectileObject(ProjectileSpawnConfig config)
        {
            GameObject projectile = null;
            if (!string.IsNullOrWhiteSpace(config.AddressableKey))
                projectile = PoolManager.Instance.Get(config.AddressableKey);
            if (projectile == null && config.FallbackPrefab != null)
                projectile = PoolManager.Instance.Get(config.FallbackPrefab);
            return projectile;
        }
    }
}
