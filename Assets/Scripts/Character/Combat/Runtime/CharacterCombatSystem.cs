using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using RPG.Character.State;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>管理单个角色的 Ability 授予、技能输入和最小普通攻击连段运行时。</summary>
    internal sealed class CharacterCombatSystem
    {
        #region 常量与依赖字段

        /// <summary>一次普通攻击成功后保留下一段连段索引的缩放时间。</summary>
        private const float NormalAttackComboRetentionDuration = 3f;

        // 依赖字段由 CharacterActor 在属性集初始化完成后注入，生命周期与该角色实例一致。
        private GameplayAbilitySystemComponent abilitySystemComponent;
        private CharacterCombatConfig combatConfig;
        private PlayerStateBlackboard stateBlackboard;
        private bool initialized;

        #endregion

        #region Ability Handle

        // key：GameplayAbilityData；value：该角色 ASC 授予后的唯一 Handle。
        private readonly Dictionary<GameplayAbilityData, GameplayAbilityHandle> abilityHandleByDataMap = new();
        // key：Sprint、Secondary 或 Skill1 至 Skill4；value：对应技能的 Handle。
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
        /// <param name="targetStateBlackboard">共享的玩家输入和移动方向快照。</param>
        /// <exception cref="ArgumentNullException">依赖为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">ASC 尚未初始化或授予 Ability 失败时抛出。</exception>
        internal void Initialize(
            GameplayAbilitySystemComponent targetAbilitySystemComponent,
            CharacterCombatConfig targetCombatConfig,
            PlayerStateBlackboard targetStateBlackboard)
        {
            if (targetAbilitySystemComponent == null)
                throw new ArgumentNullException(nameof(targetAbilitySystemComponent));
            if (targetCombatConfig == null)
                throw new ArgumentNullException(nameof(targetCombatConfig));
            if (targetStateBlackboard == null)
                throw new ArgumentNullException(nameof(targetStateBlackboard));
            if (initialized)
            {
                if (ReferenceEquals(abilitySystemComponent, targetAbilitySystemComponent) &&
                    ReferenceEquals(combatConfig, targetCombatConfig) &&
                    ReferenceEquals(stateBlackboard, targetStateBlackboard))
                    return;
                throw new InvalidOperationException("CharacterCombatSystem 已使用其他依赖完成初始化。");
            }
            if (!targetAbilitySystemComponent.IsInitialized)
                throw new InvalidOperationException("CharacterCombatSystem 无法在 ASC 属性初始化之前授予 Ability。");

            abilitySystemComponent = targetAbilitySystemComponent;
            combatConfig = targetCombatConfig;
            stateBlackboard = targetStateBlackboard;

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

        /// <summary>推进普通攻击连段保留时间，不读取或消费本帧 Ability 输入。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        /// <exception cref="ArgumentOutOfRangeException">帧时间不是非负有限值时抛出。</exception>
        /// <exception cref="InvalidOperationException">系统尚未初始化时抛出。</exception>
        internal void AdvanceFrame(float deltaTime)
        {
            if (!initialized) throw new InvalidOperationException("CharacterCombatSystem 尚未初始化。");
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "战斗帧时间必须是非负有限值。");
            AdvanceComboRetention(deltaTime);
        }

        /// <summary>按配置顺序尝试技能输入，再处理普通攻击 Press。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        /// <param name="useComboHandoff">是否把本次 Primary 普攻作为实时连段交接启动。</param>
        /// <returns>本帧有 Ability 成功激活并消费对应输入阶段时返回 true。</returns>
        /// <exception cref="ArgumentNullException">输入缓冲为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">系统尚未初始化时抛出。</exception>
        internal bool TryExecuteAbilityInput(IPlayerInputRequestBuffer inputRequests,
            bool useComboHandoff = false)
        {
            if (!initialized) throw new InvalidOperationException("CharacterCombatSystem 尚未初始化。");
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            return ProcessSkillInputRequests(inputRequests) ||
                   ProcessNormalAttackInput(inputRequests, useComboHandoff);
        }

        /// <summary>按角色配置顺序尝试技能槽位；Sprint 槽只消费短按 Click。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        /// <returns>本帧有技能成功激活并消费对应输入阶段时返回 true。</returns>
        private bool ProcessSkillInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            IReadOnlyList<CharacterAbilityInputBinding> bindings = combatConfig.SkillInputBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                CharacterAbilityInputBinding binding = bindings[index];
                if (!inputRequests.TryGetRequest(binding.InputType, out IReadOnlyPlayerInputRequest request))
                    continue;

                bool isSprintClick = binding.InputType == PlayerInputType.Sprint;
                bool stageBuffered = isSprintClick ? request.HasBufferedClick : request.HasBufferedPress;
                if (!stageBuffered) continue;
                InputRequestHandle stageHandle = isSprintClick ? request.ClickHandle : request.PressHandle;

                // QuickShift 使用同一条 Action Mixer 状态；保留短按缓冲，等待当前冲刺结束再尝试下一次。
                if (binding.InputType == PlayerInputType.Sprint && IsAbilityActive(binding.Ability))
                    continue;

                GameplayAbilityHandle handle = skillAbilityHandleByInputMap[binding.InputType];
                IReadOnlyDictionary<GameplayTag, float> setByCallerValueByTagMap =
                    TryBuildQuickShiftDirection(binding.Ability);
                if (abilitySystemComponent.TryActivateAbility(handle, setByCallerValueByTagMap, out _))
                {
                    inputRequests.TryConfirmConsumed(stageHandle);
                    return true;
                }
            }
            return false;
        }

        /// <summary>为 QuickShift 冻结当前摄像机相对的世界水平移动方向。</summary>
        /// <param name="abilityData">当前技能槽绑定的 Ability 配置。</param>
        /// <returns>QuickShift 的方向 SetByCaller 快照；其他技能返回 null。</returns>
        private IReadOnlyDictionary<GameplayTag, float> TryBuildQuickShiftDirection(
            GameplayAbilityData abilityData)
        {
            IReadOnlyList<GameplayTag> abilityTags = abilityData.AbilityTags;
            bool isQuickShift = false;
            for (int tagIndex = 0; tagIndex < abilityTags.Count; tagIndex++)
            {
                if (abilityTags[tagIndex] != GameplayTags.Tag_Skill_QuickShift) continue;
                isQuickShift = true;
                break;
            }
            if (!isQuickShift) return null;

            // 黑板方向已经是摄像机相对的世界水平向量；只快照朝向，不把摇杆幅度传入冲刺。
            Vector3 worldDirection = stateBlackboard.MoveWorldInput;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude > 0.0001f) worldDirection.Normalize();
            return new Dictionary<GameplayTag, float>
            {
                [GameplayTags.Tag_Skill_QuickShift_DirX] = worldDirection.x,
                [GameplayTags.Tag_Skill_QuickShift_DirY] = worldDirection.z
            };
        }

        /// <summary>判断指定技能是否已有运行时，防止 Sprint 槽重入复用中的 Action Mixer 状态。</summary>
        /// <param name="abilityData">当前 Sprint 绑定的 Ability 配置。</param>
        /// <returns>当前 ASC 中仍有同一 Ability Runtime 时返回 true。</returns>
        private bool IsAbilityActive(GameplayAbilityData abilityData)
        {
            IReadOnlyList<GameplayAbilityRuntime> activeAbilities = abilitySystemComponent.ActiveAbilities;
            for (int index = 0; index < activeAbilities.Count; index++)
                if (ReferenceEquals(activeAbilities[index].Spec.Data, abilityData))
                    return true;
            return false;
        }

        /// <summary>冻结当前 Primary Press 的目标段位，并尝试激活对应普通攻击。</summary>
        /// <param name="inputRequests">玩家输入请求缓冲区。</param>
        /// <param name="useComboHandoff">是否从当前普通攻击的取消窗口实时交接。</param>
        /// <returns>本帧成功激活普通攻击时返回 true。</returns>
        private bool ProcessNormalAttackInput(IPlayerInputRequestBuffer inputRequests,
            bool useComboHandoff)
        {
            if (normalAttackHandles.Count == 0)
            {
                ResetCombo();
                return false;
            }

            // 没有 Primary Press 或 Press 已被消费时清除冻结段位，下一帧会尝试新的 Press。
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
            // key：SetByCaller Tag；value：本次 Ability 是否使用 FirstActivePhase 入口。
            IReadOnlyDictionary<GameplayTag, float> activationDataByTagMap = useComboHandoff
                ? new Dictionary<GameplayTag, float>
                {
                    // 现有 Skill.ActiveAbility 只作为一次性入口标记，不写入 ASC Owner Tags。
                    [GameplayTags.Tag_Skill_ActiveAbility] = 1f
                }
                : null;
            if (!abilitySystemComponent.TryActivateAbility(handle, activationDataByTagMap, out _))
                return false;

            if (useComboHandoff)
                Debug.Log(
                    $"[CharacterCombatSystem] 普攻连段实时交接成功，index={pendingNormalAttackIndex}，入口=FirstActivePhase。",
                    abilitySystemComponent);
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
