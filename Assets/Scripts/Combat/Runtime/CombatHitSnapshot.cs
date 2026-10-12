using WS_Modules.GAS.AbilitySystemComponent;
using UnityEngine;

namespace RPG.Combat
{
    /// <summary>保存伤害提交前已经确定的来源、目标与空间命中信息。</summary>
    public readonly struct CombatHitSnapshot
    {
        #region 属性

        /// <summary>获取本次伤害来源 ASC。</summary>
        public GameplayAbilitySystemComponent Source { get; }

        /// <summary>获取已经解析并固定的实际目标 ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }

        /// <summary>获取世界空间命中点。</summary>
        public Vector3 HitPoint { get; }

        /// <summary>获取从受击者指向攻击来源的水平单位向量。</summary>
        public Vector3 IncomingDirection { get; }

        /// <summary>获取构造快照时受击者的世界旋转。</summary>
        public Quaternion TargetRotation { get; }

        /// <summary>获取 Cue 使用的世界旋转。</summary>
        public Quaternion CueRotation { get; }

        /// <summary>判断本次命中是否具有可用于方向受击的有效水平来向。</summary>
        public bool HasIncomingDirection { get; }

        #endregion

        #region 构造

        /// <summary>创建一份不可变命中快照；方向退化时保留结算及表现所需的其他数据。</summary>
        /// <param name="source">本次伤害来源 ASC。</param>
        /// <param name="target">本次实际目标 ASC。</param>
        /// <param name="hitPoint">世界空间命中点。</param>
        /// <param name="incomingDirection">从目标指向攻击来源的水平来向。</param>
        /// <param name="targetRotation">目标命中瞬间的世界旋转。</param>
        /// <param name="cueRotation">命中表现使用的世界旋转。</param>
        internal CombatHitSnapshot(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            Vector3 hitPoint,
            Vector3 incomingDirection,
            Quaternion targetRotation,
            Quaternion cueRotation)
        {
            Source = source;
            Target = target;
            HitPoint = hitPoint;
            Vector3 planarDirection = Vector3.ProjectOnPlane(incomingDirection, Vector3.up);
            HasIncomingDirection = planarDirection.sqrMagnitude > 0.0001f;
            IncomingDirection = HasIncomingDirection ? planarDirection.normalized : Vector3.zero;
            TargetRotation = targetRotation;
            CueRotation = cueRotation;
        }

        #endregion
    }
}
