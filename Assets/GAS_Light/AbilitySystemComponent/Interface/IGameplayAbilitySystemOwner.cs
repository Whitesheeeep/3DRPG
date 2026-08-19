using RPG.Character;
using RPG.Markers;
using RPG.SkillSystem;
using UnityEngine;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 定义 ASC 所属角色或实体提供给 GAS 的稳定宿主能力。
    /// </summary>
    public interface IGameplayAbilitySystemOwner
    {
        /// <summary>获取角色或实体用于表现和空间计算的根 Transform。</summary>
        Transform RootTransform { get; }

        /// <summary>获取宿主根节点上的语义 Marker Provider；不提供挂点时返回 null。</summary>
        IMarkerProvider MarkerProvider { get; }

        /// <summary>获取共享 SkillRuntimeHost 的接口。</summary>
        ISkillRuntimeHost SkillRuntimeHost { get; }

        /// <summary>获取按 ASC Tag 限制移动的接口。</summary>
        IMotionDriver MotionDriver { get; }
    }
}
