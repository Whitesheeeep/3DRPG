using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.GameplayEffect
{
    public interface IGameEffectCtrl
    {
        public GameplayAbility OwnerAbility { get; set; }
        public List<GameEffectRuntime> ActiveEffects { get; set; }
        bool CanApply();
        void Apply(AbilitySystemComponentBase source, AbilitySystemComponentBase target);
        void UnApply();
    }
}