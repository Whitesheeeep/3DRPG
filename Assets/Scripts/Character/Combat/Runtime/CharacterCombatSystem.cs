using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>管理单个角色的 Ability 授予、技能输入和最小普通攻击连段运行时。</summary>
    internal sealed class CharacterCombatSystem
    {
        #region 常量与依赖字段

        /// <summary>一次普通攻击成功后保留下一段连段索引的缩放时间。</summary>
        private const float NormalAttackComboRetentionDuration = 1f;

        // 依赖字段由 CharacterActor 在属性集初始化完成后注入，生命周期与该角色实例一致。
        private GameplayAbilitySystemComponent abilitySystemComponent;
        private CharacterCombatConfig combatConfig;
        private bool initialized;

        #endregion

        #region Ability Handle

        // key：GameplayAbilityData；value：该角色 ASC 授予后的唯一 Handle。
        private readonly Dictionary<GameplayAbilityData, GameplayAbilityHandle> abilityHandleByDataMap = new();
        // key：Secondary 或 Skill1 至 Skill4；value：对应技能的 Handle。
        private readonly Dictionary<PlayerInputType, GameplayAbilityHandle> skillAbilityHandleByInputMap = new();
        private readonly List<GameplayAbilityHandle> normalAttackHandles = new();

        #endregion

        #region 连段运行时

        // 连段索引和保留时间
        private int nextNormalAttackIndex;
        private float comboRetentionRemaining;
        private bool hasPendingNormalAttack;
        // 冻结的 Primary Press 仅在当前帧尝试激活时使用，下一帧会被新的 Press 替换。
        private InputRequestHandle pendingNormalAttackPressHandle;
        private int pendingNormalAttackIndex;

        #endregion

        #region 初始化

        /// <summary>把角色战斗配置转换为该角色 ASC 的稳定 Ability Handle。</summary>
        /// <param name="targetAbilitySystemComponent">当前角色自己的 ASC。</param>
        /// <param name="targetCombatConfig">当前角色的战斗配置。</param>
        /// <exception cref="ArgumentNullException">依赖为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">ASC 尚未初始化或授予 Ability 失败时抛出。</exception>
        internal void Initialize(
            GameplayAbilitySystemComponent targetAbilitySystemComponent,
            CharacterCombatConfig targetCombatConfig)
        {
            if (targetAbilitySystemComponent == null)
                throw new ArgumentNullException(nameof(targetAbilitySystemComponent));
            if (targetCombatConfig == null)
                throw new ArgumentNullException(nameof(targetCombatConfig));
            if (initialized)
            {
                if (ReferenceEquals(abilitySystemComponent, targetAbilitySystemComponent) &&
                    ReferenceEquals(combatConfig, targetCombatConfig))
                    return;
                throw new InvalidOperationException("CharacterCombatSystem 已使用其他依赖完成初始化。");
            }
            if (!targetAbilitySystemComponent.IsInitialized)
                throw new InvalidOperationException("CharacterCombatSystem 无法在 ASC 属性初始化之前授予 Ability。");

            abilitySystemComponent = targetAbilitySystemComponent;
            combatConfig = targetCombatConfig;

            // 普攻列表保留作者顺序；重复 Ability 只复用已授予 Handle，不重复调用 GiveAbility。
            IReadOnlyList<GameplayAbilityData> normalAttackAbilities = combatConfig.NormalAttackAbilities;
            for (int index = 0; index < normalAttackAbilities.Count; index++)
                normalAttackHandles.Add(GetOrGrantAbility(normalAttackAbilities[index]));

            IReadOnlyList<CharacterAbilityInputBinding> skillBindings = combatConfig.SkillInputBindings;
            for (int index = 0; index < skillBindings.Count; index++)
            {
                CharacterAbilityInputBinding binding = skillBindings[index];
                skillAbilityHandleByInputMap.Add(binding.InputType, GetOrGrantAbility(binding.Ability));
            }

            ResetCombo();
            initialized = true;
        }

        #endregion

        #region 输入处理

        /// <summary>按技能优先、普通攻击随后顺序处理当前角色的缓存 Press。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        /// <param name="deltaTime">本帧缩放时间，用于推进连段索引保留时间。</param>
        /// <exception cref="ArgumentNullException">输入缓冲为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">帧时间不是非负有限值时抛出。</exception>
        /// <exception cref="InvalidOperationException">系统尚未初始化时抛出。</exception>
        internal void ProcessInputRequests(IPlayerInputRequestBuffer inputRequests, float deltaTime)
        {
            if (!initialized) throw new InvalidOperationException("CharacterCombatSystem 尚未初始化。");
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "战斗帧时间必须是非负有限值。");

            ProcessSkillInputRequests(inputRequests);
            bool normalAttackActivated = ProcessNormalAttackInput(inputRequests);

            // 成功激活的帧已经把窗口重置为完整时长，不在同一帧立刻扣除一次 deltaTime。
            if (!normalAttackActivated)
                AdvanceComboRetention(deltaTime);
        }

        /// <summary>按角色配置顺序尝试技能槽位，并仅在 GAS 接受激活后消费 Press。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        private void ProcessSkillInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            IReadOnlyList<CharacterAbilityInputBinding> bindings = combatConfig.SkillInputBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                CharacterAbilityInputBinding binding = bindings[index];
                if (!inputRequests.TryGetRequest(binding.InputType, out IReadOnlyPlayerInputRequest request) ||
                    !request.HasBufferedPress)
                    continue;

                GameplayAbilityHandle handle = skillAbilityHandleByInputMap[binding.InputType];
                if (abilitySystemComponent.TryActivateAbility(handle, out _))
                    inputRequests.TryConfirmConsumed(request.PressHandle);
            }
        }

        /// <summary>冻结当前 Primary Press 的目标段位，并尝试激活对应普通攻击。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        /// <returns>本帧成功激活普通攻击时返回 true。</returns>
        private bool ProcessNormalAttackInput(IPlayerInputRequestBuffer inputRequests)
        {
            if (normalAttackHandles.Count == 0)
            {
                ResetCombo();
                return false;
            }

            if (!inputRequests.TryGetRequest(PlayerInputType.Primary, out IReadOnlyPlayerInputRequest request) ||
                !request.HasBufferedPress)
            {
                ClearPendingNormalAttack();
                return false;
            }

            if (!hasPendingNormalAttack || pendingNormalAttackPressHandle != request.PressHandle)
            {
                // 新手势到达时先结算已过期的连段窗口，再冻结这次 Press 应尝试的段位。
                if (comboRetentionRemaining <= 0f)
                    nextNormalAttackIndex = 0;
                hasPendingNormalAttack = true;
                pendingNormalAttackPressHandle = request.PressHandle;
                pendingNormalAttackIndex = nextNormalAttackIndex;
            }

            GameplayAbilityHandle handle = normalAttackHandles[pendingNormalAttackIndex];
            if (!abilitySystemComponent.TryActivateAbility(handle, out _))
                return false;

            inputRequests.TryConfirmConsumed(request.PressHandle);
            nextNormalAttackIndex = (pendingNormalAttackIndex + 1) % normalAttackHandles.Count;
            comboRetentionRemaining = NormalAttackComboRetentionDuration;
            ClearPendingNormalAttack();
            return true;
        }

        #endregion

        #region 连段状态

        /// <summary>清除普通攻击连段索引、保留时间和已冻结 Press。</summary>
        internal void ResetCombo()
        {
            nextNormalAttackIndex = 0;
            comboRetentionRemaining = 0f;
            ClearPendingNormalAttack();
        }

        /// <summary>推进连段保留时间，并在没有待重试 Press 时把下一段重置为第一段。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        private void AdvanceComboRetention(float deltaTime)
        {
            if (comboRetentionRemaining > 0f)
                comboRetentionRemaining = Math.Max(0f, comboRetentionRemaining - deltaTime);
            if (comboRetentionRemaining <= 0f && !hasPendingNormalAttack)
                nextNormalAttackIndex = 0;
        }

        /// <summary>清除当前 Primary Press 与冻结段位，不改变已经推进的下一连段索引。</summary>
        private void ClearPendingNormalAttack()
        {
            hasPendingNormalAttack = false;
            pendingNormalAttackPressHandle = default;
            pendingNormalAttackIndex = 0;
        }

        #endregion

        #region Ability 授予辅助

        /// <summary>取得指定 AbilityData 已授予的 Handle，首次遇到时向 ASC 授予一次。</summary>
        /// <param name="abilityData">待授予或复用的 Ability 配置。</param>
        /// <returns>该角色 ASC 中对应的有效 Handle。</returns>
        /// <exception cref="InvalidOperationException">ASC 拒绝授予时抛出。</exception>
        private GameplayAbilityHandle GetOrGrantAbility(GameplayAbilityData abilityData)
        {
            if (abilityHandleByDataMap.TryGetValue(abilityData, out GameplayAbilityHandle existingHandle))
                return existingHandle;

            GameplayAbilityHandle handle = abilitySystemComponent.GiveAbility(abilityData, 1);
            if (!handle.IsValid)
                throw new InvalidOperationException($"CharacterCombatSystem 无法授予 Ability '{abilityData.name}'。");
            abilityHandleByDataMap.Add(abilityData, handle);
            return handle;
        }

        #endregion
    }
}
