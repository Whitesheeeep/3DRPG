using System;
using RPG.Character.State;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>根据当前角色配置把离散 Press 请求转成能力 Intent，不直接激活能力。</summary>
    public sealed class CharacterInputIntentArbiter : GameplayInputIntentArbiter
    {
        // 动态读取当前角色，切人不重建输入仲裁器或稳定黑板。
        private readonly Func<CharacterActor> activeCharacter;

        /// <summary>创建动态角色输入策略。</summary>
        /// <param name="activeCharacter">当前角色查询。</param>
        public CharacterInputIntentArbiter(Func<CharacterActor> activeCharacter) =>
            this.activeCharacter = activeCharacter;

        /// <summary>检查当前角色配置与 Ability/Environment Tag，发布首个匹配 Intent。</summary>
        /// <param name="request">只读来源请求。</param>
        /// <param name="stage">待处理的输入阶段。</param>
        /// <param name="stateBlackboard">当前角色 Tag 与环境状态。</param>
        /// <param name="intentTag">命中时返回 Intent。</param>
        /// <returns>配置命中且允许发布时返回 true。</returns>
        protected override bool TryResolveIntent(IReadOnlyPlayerInputRequest request,
            PlayerInputRequestStage stage, PlayerStateBlackboard stateBlackboard, out GameplayTag intentTag)
        {
            intentTag = GameplayTag.Empty;
            if (stage != PlayerInputRequestStage.Press) return false;
            foreach (CharacterAbilityInputBinding binding in activeCharacter().AbilityInputBindings)
            {
                if (binding.InputType != request.InputType || !binding.CanPublish(stateBlackboard)) continue;
                intentTag = binding.IntentTag;
                return true;
            }
            return false;
        }
    }
}
