using System;
using RPG.Character;
using RPG.Character.Animation;
using RPG.Game.JumpText;
using RPG.Markers;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;

namespace RPG.Combat
{
    /// <summary>为训练木桩提供独立 ASC、伤害接收、跳字和延迟回满行为。</summary>
    [DisallowMultipleComponent]
    [InfoBox("请显式绑定本对象的 ASC 与 BasicCombatAttributeSet；命中 Collider 应位于 Enemy Layer，并在该 ASC 的层级下。")]
    public sealed class TrainingDummyController : MonoBehaviour,
        IGameplayAbilitySystemOwner,
        IGameplayAbilityTargetProvider,
        ICombatDamageReceiver
    {
        #region 依赖字段

        [SerializeField, Required, Tooltip("木桩自己的 GAS ASC。")]
        private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField, Required, Tooltip("木桩使用的正式战斗属性集合。")]
        private BasicCombatAttributeSet attributeSet;

        #endregion

        #region 木桩作者配置

        [SerializeField, MinValue(1f), Tooltip("木桩初始化与每次回满时使用的最大生命值。")]
        private float maxHealth = 10000f;
        [SerializeField, MinValue(0f), Tooltip("木桩初始化时使用的基础防御力。")]
        private float armor;

        #endregion

        #region 运行时状态

        // 持有本 ASC 在伤害管线中的唯一 Receiver 注册；清理时先注销，再清除 GE。
        private IDisposable damageReceiverRegistration;
        private bool hasStarted;
        private bool runtimeReady;
        private bool healthRestorePending;
        private bool jumpTextWarningIssued;

        #endregion

        #region GAS Owner 接口

        /// <summary>获取木桩作为命中来源和空间计算使用的根节点。</summary>
        public Transform RootTransform => transform;

        /// <summary>木桩不提供角色 Marker。</summary>
        public IMarkerProvider MarkerProvider => null;

        /// <summary>木桩不播放技能时间轴。</summary>
        public ISkillRuntimeHost SkillRuntimeHost => null;

        /// <summary>木桩不提交程序移动请求。</summary>
        public IMotionDriver MotionDriver => null;

        /// <summary>木桩不播放角色动画。</summary>
        public IAnimationPlayer AnimationPlayer => null;

        /// <summary>木桩不切换武器姿态。</summary>
        public IWeaponSwitch WeaponSwitch => null;

        /// <summary>木桩不占据 FullBody 技能动作。</summary>
        public IFullBodyActionArbiter FullBodyActionArbiter => null;

        /// <summary>木桩没有动作时间轴需要暂停。</summary>
        public bool IsActionPaused => false;

        /// <summary>木桩没有 HitStop 阶段补排动作检测。</summary>
        public bool IsActionPauseAppliedThisFrame => false;

        #endregion

        #region Unity 生命周期

        /// <summary>标记初次启用完成，避免 OnEnable 与 Start 重复初始化 ASC。</summary>
        private void Start()
        {
            hasStarted = true;
            StartRuntime();
        }

        /// <summary>重新启用时恢复满血并重新注册伤害 Receiver。</summary>
        private void OnEnable()
        {
            if (hasStarted) StartRuntime();
        }

        /// <summary>延后处理致命伤害回满，再推进 GE 和 Ability 运行时。</summary>
        private void Update()
        {
            if (!runtimeReady) return;
            if (healthRestorePending) RestoreHealthAfterDamageSettlement();
            abilitySystemComponent.Tick(Time.deltaTime);
        }

        /// <summary>沿用 ASC 的固定阶段推进接口，供以后授予固定步进 GA 时保持宿主契约完整。</summary>
        private void FixedUpdate()
        {
            if (runtimeReady) abilitySystemComponent.FixedTick(Time.fixedDeltaTime);
        }

        /// <summary>沿用 ASC 的延迟阶段推进接口，避免未来添加 Ability 后漏掉 LateTick。</summary>
        private void LateUpdate()
        {
            if (runtimeReady) abilitySystemComponent.LateTick(Time.deltaTime);
        }

        /// <summary>停用或销毁时解除伤害路由并清除木桩全部 GAS Runtime。</summary>
        private void OnDisable() => StopRuntime();

        /// <summary>确保组件单独销毁时也幂等释放注册和 ASC Runtime。</summary>
        private void OnDestroy() => StopRuntime();

        #endregion

        #region 伤害目标与接收

        /// <summary>仅在木桩启用且 ASC 已准备完成时提供实际命中目标。</summary>
        /// <returns>可以接收伤害时返回木桩 ASC，否则返回 null。</returns>
        public GameplayAbilitySystemComponent TryGetTargetASC() =>
            isActiveAndEnabled && runtimeReady && abilitySystemComponent.IsInitialized
                ? abilitySystemComponent
                : null;

        /// <summary>使用已结算伤害跳字，不再应用 GE 或修改生命值。</summary>
        /// <param name="result">由共享伤害管线生成的单次结算结果。</param>
        /// <exception cref="ArgumentNullException">伤害结果为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">结果 Target 不是本木桩 ASC 时抛出。</exception>
        public void ReceiveDamage(CombatDamageResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!ReferenceEquals(result.EffectResult.Target, abilitySystemComponent))
                throw new InvalidOperationException(
                    $"[TrainingDummy] '{name}' 收到其他 ASC 的伤害结果，target='{result.EffectResult.Target.name}'。");

            JumpTextManager jumpTextManager = JumpTextManager.Instance;
            if (jumpTextManager == null)
            {
                if (!jumpTextWarningIssued)
                {
                    jumpTextWarningIssued = true;
                    Debug.LogWarning("[TrainingDummy] 场景未初始化 JumpTextManager，木桩伤害仍已结算但不会显示跳字。", this);
                }

                return;
            }

            jumpTextWarningIssued = false;
            Vector3 worldPosition = result.HitSnapshot.HasValue
                ? result.HitSnapshot.Value.HitPoint
                : transform.position + Vector3.up;
            jumpTextManager.ShowDamage(result.FinalDamage, worldPosition, result.IsCritical);
        }

        #endregion

        #region Runtime 初始化与清理

        /// <summary>初始化基础属性、填满生命并登记目标 ASC 的 Gameplay 反馈 Receiver。</summary>
        private void StartRuntime()
        {
            if (runtimeReady || !isActiveAndEnabled) return;
            if (abilitySystemComponent == null || attributeSet == null)
                throw new InvalidOperationException(
                    $"[TrainingDummy] '{name}' 缺少显式配置的 ASC 或 BasicCombatAttributeSet。");
            if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || maxHealth <= 0f ||
                float.IsNaN(armor) || float.IsInfinity(armor) || armor < 0f)
                throw new InvalidOperationException(
                    $"[TrainingDummy] '{name}' 的基础属性无效，maxHealth={maxHealth}，armor={armor}。");

            // 首次运行导入正式 AttributeSet；再次启用只重设木桩基础值，不重建 ASC 容器。
            if (!abilitySystemComponent.IsInitialized)
                abilitySystemComponent.Initialize(new GameplayAttributeSet[] { attributeSet });
            if (!abilitySystemComponent.IsInitialized)
                throw new InvalidOperationException($"[TrainingDummy] '{name}' 的 ASC 初始化失败。");

            var baseValues = new[]
            {
                new GameplayAttributeValue(GameplayAttributes.Attribute_MaxHealth, maxHealth),
                new GameplayAttributeValue(GameplayAttributes.Attribute_Armor, armor)
            };
            if (!abilitySystemComponent.TryApplyBaseValues(baseValues))
                throw new InvalidOperationException($"[TrainingDummy] '{name}' 无法写入基础生命上限或防御力。");
            SetCurrentHealth(maxHealth);

            damageReceiverRegistration = CombatDamagePipeline.RegisterReceiver(abilitySystemComponent, this);
            abilitySystemComponent.AttributeChanged += HandleAttributeChanged;
            runtimeReady = true;
            healthRestorePending = false;
            Debug.Log(
                $"[TrainingDummy] '{name}' 已初始化并注册伤害 Receiver，maxHealth={maxHealth}，armor={armor}。",
                this);
        }

        /// <summary>注销管线接收并清除当前木桩拥有的全部运行时效果。</summary>
        private void StopRuntime()
        {
            bool hadRuntime = runtimeReady || damageReceiverRegistration != null;
            damageReceiverRegistration?.Dispose();
            damageReceiverRegistration = null;

            if (abilitySystemComponent != null && abilitySystemComponent.IsInitialized)
            {
                abilitySystemComponent.AttributeChanged -= HandleAttributeChanged;
                if (!abilitySystemComponent.IsClearingRuntime)
                    abilitySystemComponent.ClearRuntime();
            }

            runtimeReady = false;
            healthRestorePending = false;
            if (hadRuntime)
                Debug.Log($"[TrainingDummy] '{name}' 已注销伤害 Receiver 并清除 ASC Runtime。", this);
        }

        #endregion

        #region 属性变化与回满

        /// <summary>只在 Health 首次落到零时排队回满，避免在 GE 事务内部重入属性提交。</summary>
        /// <param name="attribute">发生变化的属性。</param>
        /// <param name="oldValue">变化前的 CurrentValue。</param>
        /// <param name="newValue">变化后的 CurrentValue。</param>
        private void HandleAttributeChanged(GameplayAttribute attribute, float oldValue, float newValue)
        {
            if (attribute != GameplayAttributes.Attribute_Health || newValue > 0f || oldValue <= 0f ||
                healthRestorePending)
                return;

            healthRestorePending = true;
            Debug.Log($"[TrainingDummy] '{name}' 生命归零，等待本次 GE 结算结束后回满。", this);
        }

        /// <summary>在后续 Update 阶段按当前有效 MaxHealth 恢复生命。</summary>
        private void RestoreHealthAfterDamageSettlement()
        {
            if (!abilitySystemComponent.TryGetCurrentValue(GameplayAttributes.Attribute_MaxHealth, out float effectiveMaxHealth))
                throw new InvalidOperationException($"[TrainingDummy] '{name}' 缺少 MaxHealth Attribute。");
            if (effectiveMaxHealth <= 0f) return;

            SetCurrentHealth(effectiveMaxHealth);
            healthRestorePending = false;
            Debug.Log($"[TrainingDummy] '{name}' 已在伤害结算后恢复满血，health={effectiveMaxHealth}。", this);
        }

        /// <summary>通过 ASC 的 Resource 属性接口设置 Health，并保留 AttributeSet 动态上限校验。</summary>
        /// <param name="health">需要提交的当前生命值。</param>
        private void SetCurrentHealth(float health)
        {
            var resourceValues = new[]
            {
                new GameplayAttributeValue(GameplayAttributes.Attribute_Health, health)
            };
            if (!abilitySystemComponent.TrySetResourceCurrentValues(resourceValues))
                throw new InvalidOperationException($"[TrainingDummy] '{name}' 无法设置 Health={health}。");
        }

        #endregion
    }
}
