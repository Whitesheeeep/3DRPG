using System;
using RPG.Character;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;

namespace RPG.Character.Combat
{
    /// <summary>
    /// 将命中 Cue 转换为攻击者和受击者角色上的局部卡帧请求。
    /// </summary>
    [CreateAssetMenu(fileName = "HitStopCueHandler", menuName = "RPG/Gameplay Cue Handler/Hit Stop")]
    public sealed class HitStopCueHandlerSO : GameplayCueHandlerSO
    {
        #region 类型声明

        /// <summary>获取该 Handler 支持的卡帧数据类型。</summary>
        public override Type CueDataType => typeof(HitStopCueData);

        #endregion

        #region Cue 生命周期

        /// <inheritdoc />
        public override void Execute(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller)
        {
            if (data is not HitStopCueData hitStopData)
                throw new InvalidOperationException($"HitStopCueHandlerSO 收到不匹配的数据：{data?.GetType().Name}。");

            float durationSeconds = hitStopData.DurationMilliseconds / 1000f;
            ApplyToActor(request.Source, durationSeconds);
            if (!ReferenceEquals(request.Source, request.Target))
                ApplyToActor(request.Target, durationSeconds);
        }

        #endregion

        #region 目标解析

        /// <summary>对命中来源或目标 ASC 的角色宿主提交局部暂停。</summary>
        /// <param name="asc">待暂停角色所属的 ASC。</param>
        /// <param name="durationSeconds">数据配置的暂停时长，单位为秒。</param>
        private static void ApplyToActor(WS_Modules.GAS.AbilitySystemComponent.GameplayAbilitySystemComponent asc,
            float durationSeconds)
        {
            if (asc?.Owner is IHitStopReceiver receiver)
                receiver.ApplyHitStop(durationSeconds);
        }

        #endregion
    }
}
