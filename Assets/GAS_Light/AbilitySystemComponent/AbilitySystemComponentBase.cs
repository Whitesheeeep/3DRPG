using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    public abstract class AbilitySystemComponentBase
    {
        public GameplayTagContainer Tags;
        public GameplayAttributeContainer Attributes;
        public IGameEffectCtrl GameEffectCtrl;
    }
}