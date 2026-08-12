using System;
using UnityEngine;

namespace RPG.Character.Animation
{
    /// <summary>
    /// 保存单个固定动画层可由角色资产调整的蒙版、初始权重与 Animator IK 设置。
    /// </summary>
    [Serializable]
    public sealed class AnimationLayerSettings
    {
        [SerializeField] private AvatarMask avatarMask;
        [SerializeField, Range(0f, 1f)] private float initialWeight;
        [SerializeField] private bool applyAnimatorIK;

        public AvatarMask AvatarMask => avatarMask;
        public float InitialWeight => initialWeight;
        public bool ApplyAnimatorIK => applyAnimatorIK;

        /// <summary>
        /// 使用指定初始权重创建固定层配置；其余表现参数由资产 Inspector 配置。
        /// </summary>
        /// <param name="initialWeight">角色创建时应用到 Animancer Layer 的权重。</param>
        public AnimationLayerSettings(float initialWeight)
        {
            this.initialWeight = initialWeight;
        }
    }

    /// <summary>
    /// 配置 Base、Action、UpperBody 与 Additive 四个固定 Animancer 层的角色差异参数。
    /// </summary>
    [CreateAssetMenu(fileName = "AnimationLayerProfile", menuName = "RPG/Character/Animation Layer Profile")]
    public sealed class AnimationLayerProfile : ScriptableObject
    {
        [Header("固定动画层")]
        [SerializeField] private AnimationLayerSettings baseLayer = new(1f);
        [SerializeField] private AnimationLayerSettings actionLayer = new(0f);
        [SerializeField] private AnimationLayerSettings upperBodyLayer = new(0f);
        [SerializeField] private AnimationLayerSettings additiveLayer = new(0f);

        /// <summary>
        /// 获取固定语义层对应的角色表现配置。
        /// </summary>
        /// <param name="layer">固定动画层。</param>
        /// <returns>该层的蒙版、初始权重与 IK 配置。</returns>
        /// <exception cref="ArgumentOutOfRangeException">传入未定义的固定层枚举值。</exception>
        public AnimationLayerSettings GetSettings(AnimationLayerType layer)
        {
            return layer switch
            {
                AnimationLayerType.Base => baseLayer,
                AnimationLayerType.Action => actionLayer,
                AnimationLayerType.UpperBody => upperBodyLayer,
                AnimationLayerType.Additive => additiveLayer,
                _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "未定义的固定动画层。")
            };
        }
    }
}
