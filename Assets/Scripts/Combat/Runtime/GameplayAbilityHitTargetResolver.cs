using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.Combat
{
    /// <summary>沿命中 Collider 的父级查找最近目标提供者，并校验其返回的 ASC。</summary>
    public static class GameplayAbilityHitTargetResolver
    {
        #region 公开操作
        /// <summary>将碰撞体解析为实际且已经初始化的技能受击 ASC。</summary>
        /// <param name="collider">攻击检测或投射物命中的原始碰撞体。</param>
        /// <returns>最近 Provider 接受该命中且返回有效 ASC 时为 true。</returns>
        /// <exception cref="InvalidOperationException">同一层级存在多个目标 Provider 时抛出。</exception>
        public static GameplayAbilitySystemComponent TryResolve(Collider collider)
        {
            if (collider == null) return null;

            GameplayAbilitySystemComponent target = null;
            // 仅沿命中对象的祖先链解析；Provider 拒绝命中时不再向外寻找其他 ASC。
            Transform current = collider.transform;

            return current.GetComponentInParent<IGameplayAbilityTargetProvider>()?.TryGetTargetASC();
        }

        #endregion
    }
}
