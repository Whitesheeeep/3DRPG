using System;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character.Animation
{
    /// <summary>
    /// 为单个角色初始化并管理固定 Animancer 层，向 FSM、技能及受击系统提供统一播放入口。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator), typeof(AnimancerComponent))]
    [InfoBox("依赖同节点 Animator 与 AnimancerComponent；AnimationLayerProfile 决定四个固定动画层的蒙版、权重和 IK 配置。")]
    public sealed class AnimationController : MonoBehaviour, IAnimationPlayer
    {
        #region 配置与状态

        private const int LayerCount = 4;

        // 同节点 Animator/Animancer 负责实际播放，Profile 提供固定层配置。
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private AnimationLayerProfile profile;

        private bool initialized;

        #endregion

        #region 生命周期

        /// <summary>
        /// 在角色其他系统开始播放动画前创建并配置固定四层。
        /// </summary>
        private void Awake()
        {
            InitializeIfNeeded();
        }

        /// <summary>在 Inspector 新增组件时自动填充同节点 Animancer 引用。</summary>
        private void Reset()
        {
            animancer = GetComponent<AnimancerComponent>();
            profile = null;
        }
        #endregion

        #region 播放

        /// <summary>
        /// 在指定固定层播放 AnimationClip，并同时把目标层淡入到完全权重。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="clip">需要播放的动画素材。</param>
        /// <param name="fadeDuration">新旧状态及目标层淡入所使用的秒数。</param>
        /// <param name="fadeMode">Animancer 状态淡入模式。</param>
        /// <returns>本次播放得到的真实 Animancer 状态。</returns>
        /// <exception cref="ArgumentNullException">动画素材为空。</exception>
        public AnimancerState Play(AnimationLayerType layer, AnimationClip clip, float fadeDuration = 0f,
            FadeMode fadeMode = FadeMode.FromStart)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));

            AnimancerLayer animancerLayer = GetLayer(layer);
            AnimancerState state = animancer.States.GetOrCreate(clip);
            state = animancerLayer.Play(state, Mathf.Max(0f, fadeDuration), fadeMode);
            ActivateLayer(animancerLayer, fadeDuration);
            return state;
        }

        /// <summary>
        /// 在指定固定层播放 Animancer Transition，并使用其淡入时间激活目标层。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="transition">包含动画、过渡与事件配置的 Animancer Transition。</param>
        /// <returns>本次播放得到的真实 Animancer 状态。</returns>
        /// <exception cref="ArgumentNullException">Transition 为空。</exception>
        public AnimancerState Play(AnimationLayerType layer, ITransition transition)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));

            AnimancerLayer animancerLayer = GetLayer(layer);
            AnimancerState state = animancerLayer.Play(transition);
            ActivateLayer(animancerLayer, transition.FadeDuration);
            return state;
        }

        /// <summary>将浮点参数写入当前角色的 Animancer 参数表。</summary>
        /// <param name="parameter">参数资产；为空时直接暴露配置错误。</param>
        /// <param name="value">参数值。</param>
        public void SetFloatParameter(StringAsset parameter, float value)
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            animancer.Parameters.SetValue(parameter, value);
        }

        #endregion

        #region 层控制

        /// <summary>
        /// 将指定固定层平滑调整到目标权重，但保留该层当前状态及播放进度。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        /// <param name="targetWeight">限制在零到一之间的目标权重。</param>
        /// <param name="duration">权重过渡秒数；零表示立即设置。</param>
        public void FadeLayer(AnimationLayerType layer, float targetWeight, float duration)
        {
            AnimancerLayer animancerLayer = GetLayer(layer);
            float clampedWeight = Mathf.Clamp01(targetWeight);
            if (duration <= 0f)
            {
                animancerLayer.Weight = clampedWeight;
                return;
            }

            animancerLayer.StartFade(clampedWeight, duration);
        }

        /// <summary>
        /// 立即停止指定固定层并将其权重归零；后续默认动画由外部状态机决定。
        /// </summary>
        /// <param name="layer">目标固定动画层。</param>
        public void StopLayer(AnimationLayerType layer)
        {
            AnimancerLayer animancerLayer = GetLayer(layer);
            animancerLayer.Stop();
            animancerLayer.Weight = 0f;
        }

        #endregion

        #region 初始化与查询

        /// <summary>
        /// 初始化 Animancer 引用和固定四层；公开操作也调用该方法以消除 MonoBehaviour Awake 顺序差异。
        /// </summary>
        /// <exception cref="InvalidOperationException">角色未配置 AnimationLayerProfile。</exception>
        private void InitializeIfNeeded()
        {
            if (initialized) return;
            if (profile == null)
                throw new InvalidOperationException($"角色 {name} 未配置 AnimationLayerProfile。");

            if (animancer == null) animancer = GetComponent<AnimancerComponent>();
            animancer.Animator = GetComponent<Animator>();

            // 枚举数值是固定层协议，因此严格按 0..3 初始化，不读取可重排列表。
            for (int index = 0; index < LayerCount; index++)
            {
                AnimationLayerType layer = (AnimationLayerType)index;
                ConfigureLayer(animancer.Layers[index], layer, profile.GetSettings(layer));
            }

            initialized = true;
        }

        /// <summary>
        /// 将固定语义层配置应用到对应 Animancer Layer。
        /// </summary>
        /// <param name="layer">需要配置的 Animancer Layer。</param>
        /// <param name="type">决定索引、名称及 Additive 语义的固定层。</param>
        /// <param name="settings">角色资产提供的蒙版、初始权重与 IK 参数。</param>
        private static void ConfigureLayer(AnimancerLayer layer, AnimationLayerType type,
            AnimationLayerSettings settings)
        {
            layer.Mask = settings.AvatarMask;
            layer.IsAdditive = type == AnimationLayerType.Additive;
            layer.ApplyAnimatorIK = settings.ApplyAnimatorIK;
            layer.Weight = Mathf.Clamp01(settings.InitialWeight);
            layer.SetDebugName(type.ToString());
        }

        /// <summary>
        /// 获取固定枚举值对应的 Animancer Layer。
        /// </summary>
        /// <param name="layer">固定动画层。</param>
        /// <returns>已完成配置的 Animancer Layer。</returns>
        /// <exception cref="ArgumentOutOfRangeException">传入未定义的固定层枚举值。</exception>
        private AnimancerLayer GetLayer(AnimationLayerType layer)
        {
            InitializeIfNeeded();
            int index = (int)layer;
            if (index < 0 || index >= LayerCount)
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "未定义的固定动画层。");
            return animancer.Layers[index];
        }

        /// <summary>
        /// 使用本次播放的淡入时间把目标层恢复到完全权重。
        /// </summary>
        /// <param name="layer">刚刚接收新状态的 Animancer Layer。</param>
        /// <param name="fadeDuration">层权重淡入秒数。</param>
        private static void ActivateLayer(AnimancerLayer layer, float fadeDuration)
        {
            if (fadeDuration <= 0f)
            {
                layer.Weight = 1f;
                return;
            }

            layer.StartFade(1f, fadeDuration);
        }

        #endregion
    }
}
