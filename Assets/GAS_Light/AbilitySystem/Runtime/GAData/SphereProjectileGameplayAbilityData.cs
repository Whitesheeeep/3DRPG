using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>
    /// 创建可独立移动并在首次物理命中时应用 Ability Effects 的球体投射物示例。
    /// </summary>
    [CreateAssetMenu(fileName = "SphereProjectileGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Projectile/Sphere Test")]
    public sealed class SphereProjectileGameplayAbilityData : ProjectileGameplayAbilityData
    {
        #region 字段

        [SerializeField, Min(0.01f), Tooltip("生成球体的半径。")]
        private float radius = 0.25f;

        [SerializeField, Min(0f), Tooltip("球体沿 Source 前方移动的速度。")]
        private float speed = 5f;

        [SerializeField, Min(0f), Tooltip("球体未命中目标时的最长存活时间。")]
        private float lifetime = 2f;

        #endregion

        #region 同步执行

        // 使用本次 Runtime 的 Source Transform 创建投射物；投射物后续生命周期不延长 GA Runtime。
        protected override void SpawnProjectile(SynchronousGameplayAbilityRuntime runtime)
        {
            Transform sourceTransform = runtime.Source.transform;
            Vector3 direction = sourceTransform.forward.sqrMagnitude > 0f
                ? sourceTransform.forward.normalized
                : Vector3.forward;

            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "GA Sphere Projectile (Test)";
            projectile.transform.SetPositionAndRotation(
                sourceTransform.position,
                Quaternion.LookRotation(direction));
            projectile.transform.localScale = Vector3.one * (radius * 2f);

            SphereCollider collider = projectile.GetComponent<SphereCollider>();
            collider.isTrigger = true;

            Rigidbody body = projectile.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            SphereProjectileProbe probe = projectile.AddComponent<SphereProjectileProbe>();
            probe.Initialize(
                body,
                direction,
                speed,
                lifetime,
                runtime.Source,
                runtime.Level,
                runtime.SetByCaller,
                Effects);
        }

        #endregion

        #region 投射物实例

        /// <summary>
        /// 保存单次投射物的激活快照，并通过真实 Trigger 碰撞向目标 ASC 应用 Effects。
        /// </summary>
        private sealed class SphereProjectileProbe : MonoBehaviour
        {
            private Rigidbody body;
            private Vector3 direction;
            private float speed;
            private float remainingLife;
            private GameplayAbilitySystemComponent source;
            private int level;
            private IReadOnlyDictionary<GameplayTag, float> setByCaller;
            private GameplayEffectData[] effects;
            private bool hit;

            // 复制本次激活所需数据，避免投射物继续依赖可被其他激活覆盖的 SO 运行时字段。
            internal void Initialize(
                Rigidbody projectileBody,
                Vector3 moveDirection,
                float moveSpeed,
                float life,
                GameplayAbilitySystemComponent sourceAsc,
                int abilityLevel,
                IReadOnlyDictionary<GameplayTag, float> callerValues,
                IReadOnlyList<GameplayEffectData> configuredEffects)
            {
                body = projectileBody;
                direction = moveDirection;
                speed = moveSpeed;
                remainingLife = life;
                source = sourceAsc;
                level = abilityLevel;
                setByCaller = callerValues;

                effects = new GameplayEffectData[configuredEffects.Count];
                for (int i = 0; i < configuredEffects.Count; i++)
                    effects[i] = configuredEffects[i];
            }

            // 在物理步中移动并累计存活时间，确保 Trigger 检测与位置更新使用相同节奏。
            private void FixedUpdate()
            {
                if (hit) return;

                body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));
                remainingLife -= Time.fixedDeltaTime;
                if (remainingLife <= 0f) Destroy(gameObject);
            }

            // 首次命中其他 ASC 时逐项应用快照 Effects，并终止投射物生命周期。
            private void OnTriggerEnter(Collider other)
            {
                if (hit) return;

                GameplayAbilitySystemComponent target =
                    other.GetComponentInParent<GameplayAbilitySystemComponent>();
                if (target == null || ReferenceEquals(target, source)) return;

                hit = true;
                for (int i = 0; i < effects.Length; i++)
                {
                    GameplayEffectData effect = effects[i];
                    if (effect != null)
                        target.TryApplyEffect(effect, source, level, setByCaller, out _);
                }

                Destroy(gameObject);
            }
        }

        #endregion
    }
}
