using System.Collections.Generic;
using RPG.Combat;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存单次池化投射物快照，通过 Rigidbody 移动并在首次有效碰撞时结算 Effects。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [InfoBox("依赖同节点 Rigidbody 和 Collider；RequireComponent 会在缺失时由 Unity 自动补齐。投射物命中目标时通过父级 Gameplay Provider 解析目标 ASC，未找到有效 Provider 的 Collider 会被忽略。")]
    public sealed class GameplayAbilityProjectileBehaviour : PoolObjectIdentity
    {
        #region 字段

        private Rigidbody projectileBody;
        private GameplayAbilitySystemComponent source;
        private GameplayEffectSpec[] effectSpecs;
        private GameplayTag[] cueTags;
        private GameplayAbilityRuntime abilityRuntime;
        private Vector3 direction;
        private float speed;
        private float remainingLifetime;
        private LayerMask targetLayerMask = ~0;
        private bool running;

        #endregion

        #region Unity 生命周期

        /// <summary>缓存投射物刚体并固定碰撞所需配置。</summary>
        protected override void Awake()
        {
            base.Awake();
            projectileBody = GetComponent<Rigidbody>();
            Collider projectileCollider = GetComponent<Collider>();
            projectileCollider.isTrigger = true;
            projectileBody.useGravity = false;
            projectileBody.isKinematic = true;
            projectileBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        /// <summary>在物理帧中移动，并在存活时间耗尽时归还对象池。</summary>
        private void FixedUpdate()
        {
            if (!running) return;
            projectileBody.MovePosition(
                projectileBody.position + direction * (speed * Time.fixedDeltaTime));
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f) Recycle();
        }

        /// <summary>首次命中有效业务目标后逐项应用 Effects、发布命中 Cue 并回收。</summary>
        /// <param name="other">进入 Trigger 的碰撞体。</param>
        private void OnTriggerEnter(Collider other)
        {
            if (!running) return;

            var target  = GameplayAbilityHitTargetResolver.TryResolve(other);
            if (target == null || target == source)
                return;
            if ((targetLayerMask.value & (1 << target.gameObject.layer)) == 0) return;

            running = false;
            Transform targetRoot = target.Owner.RootTransform;
            // 投射物来袭方向是飞行方向的反向；Cue 继续使用投射物当前世界旋转。
            Vector3 incomingDirection = Vector3.ProjectOnPlane(-direction, Vector3.up);
            var hitSnapshot = new CombatHitSnapshot(
                source,
                target,
                transform.position,
                incomingDirection,
                targetRoot.rotation,
                transform.rotation);

            int appliedEffectCount = 0;
            for (int i = 0; i < effectSpecs.Length; i++)
            {
                if (effectSpecs[i] == null ||
                    !CombatDamagePipeline.TryApplyHitEffect(
                        effectSpecs[i], hitSnapshot, out GameplayEffectApplicationResult applicationResult))
                    continue;

                appliedEffectCount++;
                CombatHitCuePublisher.Publish(cueTags, hitSnapshot, abilityRuntime, applicationResult);
            }

            if (appliedEffectCount == 0)
            {
                // 无 GE 或全部应用失败仍允许命中表现，但不附加错误的 GE Result。
                CombatHitCuePublisher.Publish(cueTags, hitSnapshot, abilityRuntime, null);
            }

            Debug.Log(
                $"[GameplayAbilityProjectile] 命中已结算，targetASC='{target.name}'，appliedEffects={appliedEffectCount}。",
                target);

            Recycle();
        }

        #endregion

        #region 初始化与回收

        /// <summary>对象从池中取出时停止运行并清除上一次刚体运动状态。</summary>
        protected override void OnSpawn()
        {
            running = false;
            // projectileBody.velocity = Vector3.zero;
            // projectileBody.angularVelocity = Vector3.zero;
        }

        /// <summary>对象归还池前停止运行并释放本次 Ability 激活快照。</summary>
        protected override void OnDespawn()
        {
            running = false;
            // projectileBody.velocity = Vector3.zero;
            //projectileBody.angularVelocity = Vector3.zero;
            source = null;
            effectSpecs = null;
            cueTags = null;
            abilityRuntime = null;
            targetLayerMask = ~0;
        }

        /// <summary>复制单次 Ability 激活数据并启动投射物。</summary>
        /// <param name="sourceAsc">发射投射物的 Source ASC。</param>
        /// <param name="configuredEffectSpecs">命中时应用的已封存 GE Specs。</param>
        /// <param name="configuredCueTags">命中时发布的 CueTags。</param>
        /// <param name="sourceRuntime">生成投射物的 Ability Runtime。</param>
        /// <param name="spawnPosition">本次激活的刚体世界位置。</param>
        /// <param name="spawnRotation">本次激活的刚体世界旋转。</param>
        /// <param name="moveDirection">投射物世界移动方向。</param>
        /// <param name="moveSpeed">移动速度。</param>
        /// <param name="lifetime">最长存活秒数。</param>
        /// <param name="targetMask">允许投射物结算的目标 ASC 根节点 LayerMask。</param>
        public void Initialize(
            GameplayAbilitySystemComponent sourceAsc,
            IReadOnlyList<GameplayEffectSpec> configuredEffectSpecs,
            IReadOnlyList<GameplayTag> configuredCueTags,
            GameplayAbilityRuntime sourceRuntime,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Vector3 moveDirection,
            float moveSpeed,
            float lifetime,
            LayerMask targetMask)
        {
            // 池化对象可能保留上一次物理 Pose；在写入运行数据前先阻止 FixedUpdate 消费半初始化状态。
            running = false;
            // Transform 负责当前帧显示，Rigidbody Pose 负责下一物理帧；插值刚体初始化时必须同时提交两者。
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            projectileBody.position = spawnPosition;
            projectileBody.rotation = spawnRotation;


            source = sourceAsc;
            abilityRuntime = sourceRuntime;
            direction = moveDirection.normalized;
            speed = moveSpeed;
            remainingLifetime = lifetime;
            targetLayerMask = targetMask;

            effectSpecs = new GameplayEffectSpec[configuredEffectSpecs?.Count ?? 0];
            for (int i = 0; i < effectSpecs.Length; i++)
                effectSpecs[i] = configuredEffectSpecs[i];

            cueTags = new GameplayTag[configuredCueTags.Count];
            for (int i = 0; i < configuredCueTags.Count; i++)
                cueTags[i] = configuredCueTags[i];

            // 所有 Pose 与运行快照提交完毕后才允许物理帧推进。
            running = true;
        }

        /// <summary>结束本次投射物运行并通过 PoolManager 归还实例。</summary>
        private void Recycle()
        {
            if (!running && source == null) return;
            running = false;
            PoolManager.Instance.Recycle(gameObject);
        }

        #endregion
    }
}
