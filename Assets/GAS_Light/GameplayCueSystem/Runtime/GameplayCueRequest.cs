using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>描述一次 GameplayCue 请求以及它的来源和空间信息。</summary>
    public readonly struct GameplayCueRequest
    {
        #region 请求数据
        /// <summary>本次请求对应的 CueTag。</summary>
        public GameplayTag CueTag { get; }
        /// <summary>本次请求要执行的表现阶段。</summary>
        public GameplayCueEventType EventType { get; }
        /// <summary>产生请求的来源 ASC。</summary>
        public GameplayAbilitySystemComponent Source { get; }
        /// <summary>接收请求的目标 ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }
        /// <summary>产生请求的 Gameplay Effect Runtime。</summary>
        public GameEffectRuntime EffectRuntime { get; }
        /// <summary>产生请求的 Gameplay Ability Runtime。</summary>
        public GameplayAbilityRuntime AbilityRuntime { get; }
        /// <summary>显式世界位置。</summary>
        public Vector3 Position { get; }
        /// <summary>显式世界旋转。</summary>
        public Quaternion Rotation { get; }
        /// <summary>动态挂点。</summary>
        public Transform AttachTransform { get; }
        /// <summary>是否提供了显式空间位置。</summary>
        public bool HasExplicitPlacement { get; }
        #endregion

        #region 构造函数
        /// <summary>创建不带额外空间覆盖的 Cue 请求。</summary>
        public GameplayCueRequest(
            GameplayTag cueTag,
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime effectRuntime = null,
            GameplayAbilityRuntime abilityRuntime = null)
            : this(cueTag, eventType, source, target, effectRuntime, abilityRuntime,
                Vector3.zero, Quaternion.identity, null, false)
        {
        }

        /// <summary>创建带世界位置、旋转或挂点的 Cue 请求。</summary>
        public GameplayCueRequest(
            GameplayTag cueTag,
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime effectRuntime,
            GameplayAbilityRuntime abilityRuntime,
            Vector3 position,
            Quaternion rotation,
            Transform attachTransform = null)
            : this(cueTag, eventType, source, target, effectRuntime, abilityRuntime,
                position, rotation, attachTransform, true)
        {
        }

        private GameplayCueRequest(
            GameplayTag cueTag,
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime effectRuntime,
            GameplayAbilityRuntime abilityRuntime,
            Vector3 position,
            Quaternion rotation,
            Transform attachTransform,
            bool hasExplicitPlacement)
        {
            CueTag = cueTag;
            EventType = eventType;
            Source = source;
            Target = target;
            EffectRuntime = effectRuntime;
            AbilityRuntime = abilityRuntime;
            Position = position;
            Rotation = rotation;
            AttachTransform = attachTransform;
            HasExplicitPlacement = hasExplicitPlacement;
        }
        #endregion
    }
}
