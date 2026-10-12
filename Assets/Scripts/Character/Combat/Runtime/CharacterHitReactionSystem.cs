using System;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.TAG;
using UnityEngine;
using RPG.Combat;

namespace RPG.Character
{
    /// <summary>集中保存本次受击流程使用的稳定 GameplayTag ID。</summary>
    internal static class HitReactionGameplayTags
    {
        /// <summary>基础伤害 GE 的受击识别 GrantedTag。</summary>
        internal static readonly GameplayTag NormalDamageMarker = GameplayTags.Tag_HitReaction_Normal;
        /// <summary>当前激活携带的单次受击方向数值标签。</summary>
        internal static readonly GameplayTag Direction = GameplayTags.Tag_Skill_HitReaction_Direction;
    }

    /// <summary>消费 Actor 收到的伤害结果，只为当前上场角色解析受击动画 GA。</summary>
    internal sealed class CharacterHitReactionSystem : IDisposable
    {
        #region 依赖字段

        // 系统仅观察所属角色与其战斗配置，不缓存全局 Player 或其他角色状态。
        private readonly CharacterActor owner;
        private readonly CharacterCombatSystem combatSystem;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>绑定所属角色与已经授予受击 GA 的战斗系统。</summary>
        /// <param name="character">拥有 ASC 与当前动作状态的角色。</param>
        /// <param name="sourceCombatSystem">已经授予受击 GA 的战斗系统。</param>
        /// <exception cref="ArgumentNullException">角色或战斗系统为空时抛出。</exception>
        internal CharacterHitReactionSystem(CharacterActor character, CharacterCombatSystem sourceCombatSystem)
        {
            owner = character ?? throw new ArgumentNullException(nameof(character));
            combatSystem = sourceCombatSystem ?? throw new ArgumentNullException(nameof(sourceCombatSystem));
            Debug.Log($"[CharacterHitReactionSystem] 已绑定角色 '{owner.name}' 的 Gameplay 伤害反馈入口。", owner);
        }

        /// <summary>停止接收 CharacterActor 转发的伤害结果。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Debug.Log($"[CharacterHitReactionSystem] 已解除角色 '{owner.name}' 的 Gameplay 伤害反馈绑定。", owner);
        }

        #endregion

        #region 伤害解析

        /// <summary>只处理有效来向、正伤害及匹配 GE GrantedTag 的当前玩家命中。</summary>
        /// <param name="result">由伤害管线按实际 Target ASC 直接路由的 Gameplay 伤害结果。</param>
        internal void ReceiveDamage(CombatDamageResult result)
        {
            if (disposed || owner.IsDead || result?.EffectResult.Target != owner.AbilitySystemComponent ||
                !result.HitSnapshot.HasValue || !result.HitSnapshot.Value.HasIncomingDirection ||
                !owner.IsActiveCharacter)
                return;

            if (!TryResolveConfiguredMarker(
                    result.EffectResult.Spec.Data.GrantedTags, out GameplayTag effectTag))
                return;

            GameplayAbilityRuntime currentRuntime = owner.CurrentFullBodyRuntime;
            if (currentRuntime != null && !currentRuntime.IsCancelable)
            {
                Debug.Log(
                    $"[CharacterHitReactionSystem] 伤害已结算但当前 Runtime 禁止普通取消，跳过受击，" +
                    $"activationId={currentRuntime.ActivationId}，finalDamage={result.FinalDamage:F1}。",
                    owner);
                return;
            }

            E_HitReactionDirection direction = ResolveDirection(result.HitSnapshot.Value);
            bool activated = combatSystem.TryActivateHitReaction(effectTag, (float)direction);
            Debug.Log(
                $"[CharacterHitReactionSystem] 受击解析完成，tagId={effectTag.Id}，" +
                $"direction={direction}，critical={result.IsCritical}，activated={activated}，" +
                $"damage={result.FinalDamage:F1}。",
                owner);
        }

        /// <summary>按角色配置顺序查找与 GE GrantedTags 精确相等的受击身份。</summary>
        /// <param name="grantedTags">本次 GE 的作者配置标签。</param>
        /// <param name="effectTag">命中时返回第一个对应受击 GA 的标记。</param>
        /// <returns>GE 带有已配置标记时返回 true。</returns>
        private bool TryResolveConfiguredMarker(
            System.Collections.Generic.IReadOnlyList<GameplayTag> grantedTags,
            out GameplayTag effectTag)
        {
            System.Collections.Generic.IReadOnlyList<CharacterHitReactionBinding> bindings =
                owner.Config.CombatConfig.HitReactionBindings;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                GameplayTag configuredTag = bindings[bindingIndex].EffectTag;
                for (int tagIndex = 0; tagIndex < grantedTags.Count; tagIndex++)
                {
                    if (grantedTags[tagIndex] != configuredTag) continue;
                    effectTag = configuredTag;
                    return true;
                }
            }

            effectTag = GameplayTag.Empty;
            return false;
        }

        /// <summary>将世界空间来袭方向转换为命中瞬间目标局部空间中的最近四向。</summary>
        /// <param name="hitSnapshot">不可变的本次 Gameplay 命中快照。</param>
        /// <returns>用于 Transition 索引的前、后、左或右方向。</returns>
        private static E_HitReactionDirection ResolveDirection(CombatHitSnapshot hitSnapshot)
        {
            Vector3 localDirection = Quaternion.Inverse(hitSnapshot.TargetRotation) * hitSnapshot.IncomingDirection;
            localDirection = Vector3.ProjectOnPlane(localDirection, Vector3.up);
            if (Mathf.Abs(localDirection.x) > Mathf.Abs(localDirection.z))
                return localDirection.x < 0f ? E_HitReactionDirection.Left : E_HitReactionDirection.Right;
            return localDirection.z < 0f ? E_HitReactionDirection.Backward : E_HitReactionDirection.Forward;
        }

        #endregion
    }
}
