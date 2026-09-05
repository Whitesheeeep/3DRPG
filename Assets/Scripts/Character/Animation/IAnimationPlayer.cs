using Animancer;
using UnityEngine;

namespace RPG.Character.Animation
{
    /// <summary>
    /// 定义角色固定语义层的 Animancer 播放与层权重控制入口。
    /// </summary>
    public interface IAnimationPlayer
    {
        /// <summary>
        /// 在指定固定层播放 AnimationClip，并返回可由调用方设置时间、速度和事件的真实状态。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="clip">需要播放的动画素材。</param>
        /// <param name="fadeDuration">新旧状态及目标层淡入所使用的秒数。</param>
        /// <param name="fadeMode">Animancer 状态淡入模式。</param>
        /// <returns>本次播放得到的 Animancer 状态。</returns>
        AnimancerState Play(AnimationLayerType layer, AnimationClip clip, float fadeDuration = 0f,
            FadeMode fadeMode = FadeMode.FromStart);

        /// <summary>
        /// 在指定固定层播放 Animancer Transition，并返回其真实状态。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="transition">包含动画、过渡与事件配置的 Animancer Transition。</param>
        /// <returns>本次播放得到的 Animancer 状态。</returns>
        AnimancerState Play(AnimationLayerType layer, ITransition transition);

        /// <summary>
        /// 在指定固定层播放 Animancer Transition，并允许调用方覆盖本次播放的淡入时长。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="transition">包含动画、事件和默认播放参数的 Transition。</param>
        /// <param name="fadeDuration">本次播放使用的淡入秒数；零表示直接切换。</param>
        /// <returns>本次播放得到的真实 Animancer 状态。</returns>
        AnimancerState Play(AnimationLayerType layer, ITransition transition, float fadeDuration);

        /// <summary>设置指定 Animancer 参数的浮点值。</summary>
        /// <param name="parameter">共享的参数资产。</param>
        /// <param name="value">要写入的参数值。</param>
        void SetFloatParameter(StringAsset parameter, float value);

        /// <summary>
        /// 将指定固定层平滑调整到目标权重，不停止该层当前状态。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="targetWeight">限制在零到一之间的目标权重。</param>
        /// <param name="duration">权重过渡秒数；零表示立即设置。</param>
        void FadeLayer(AnimationLayerType layer, float targetWeight, float duration);

        /// <summary>
        /// 立即停止指定固定层并将其权重归零。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        void StopLayer(AnimationLayerType layer);
    }
}
