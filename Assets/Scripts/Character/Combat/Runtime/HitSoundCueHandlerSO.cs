using System;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.AudioSystem;

namespace RPG.Character.Combat
{
    /// <summary>
    /// 在有效命中位置按资源 Key 播放一次性 SFX。
    /// </summary>
    [CreateAssetMenu(fileName = "HitSoundCueHandler", menuName = "RPG/Gameplay Cue Handler/Hit Sound")]
    public sealed class HitSoundCueHandlerSO : GameplayCueHandlerSO
    {
        #region 类型声明

        /// <summary>获取该 Handler 支持的命中音效数据类型。</summary>
        public override Type CueDataType => typeof(HitSoundCueData);

        #endregion

        #region Cue 生命周期

        /// <inheritdoc />
        public override void Execute(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller)
        {
            if (data is not HitSoundCueData hitSoundData)
                throw new InvalidOperationException($"HitSoundCueHandlerSO 收到不匹配的数据：{data?.GetType().Name}。");
            if (string.IsNullOrWhiteSpace(hitSoundData.AudioKey))
                throw new InvalidOperationException($"HitSoundCueData '{hitSoundData.name}' 没有音频 Key。");

            AudioManager.Instance.PlaySFX(
                hitSoundData.AudioKey,
                request.Position,
                hitSoundData.Spatial,
                hitSoundData.Volume,
                loop: false,
                autoRelease: true);
            Debug.Log(
                $"[HitSoundCueHandlerSO] 播放命中音效 Key='{hitSoundData.AudioKey}'，position={request.Position}。",
                request.Target);
        }

        #endregion
    }
}
