using Cinemachine;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 保存一次 Uniform Cinemachine Impulse 的通道、波形、时长与默认力度。
    /// </summary>
    [CreateAssetMenu(fileName = "CameraImpulseProfile", menuName = "RPG/Camera/Impulse Profile")]
    public sealed class CameraImpulseProfile : ScriptableObject
    {
        #region 序列化配置

        [SerializeField, LabelText("Impulse 通道")]
        private int channel = 1;

        [SerializeField, LabelText("冲击波形")]
        private CinemachineImpulseDefinition.ImpulseShapes shape = CinemachineImpulseDefinition.ImpulseShapes.Bump;

        [SerializeField, LabelText("自定义曲线"), ShowIf(nameof(IsCustom))]
        private AnimationCurve customCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);

        [SerializeField, LabelText("冲击时长"), MinValue(0.001f)]
        private float duration = 0.2f;

        [SerializeField, LabelText("默认强度"), MinValue(0f)]
        private float defaultAmplitude = 1f;

        [SerializeField, LabelText("默认方向")]
        private Vector3 defaultDirection = Vector3.down;

        #endregion

        #region 属性

        public int Channel => channel;
        public CinemachineImpulseDefinition.ImpulseShapes Shape => shape;
        public AnimationCurve CustomCurve => customCurve;
        public float Duration => duration;
        public float DefaultAmplitude => defaultAmplitude;
        public Vector3 DefaultDirection => defaultDirection;
        private bool IsCustom => shape == CinemachineImpulseDefinition.ImpulseShapes.Custom;

        #endregion

        #region Definition 创建

        /// <summary>
        /// 为单次事件创建独立 Definition，避免多个调用共享并修改可变 Cinemachine 配置。
        /// </summary>
        /// <returns>固定为 Uniform 传播模式的独立 Definition。</returns>
        internal CinemachineImpulseDefinition CreateDefinition()
        {
            return new CinemachineImpulseDefinition
            {
                m_ImpulseChannel = channel,
                m_ImpulseShape = shape,
                m_CustomImpulseShape = customCurve != null
                    ? new AnimationCurve(customCurve.keys)
                    : AnimationCurve.EaseInOut(0f, 0f, 1f, 0f),
                m_ImpulseDuration = Mathf.Max(0.001f, duration),
                m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform
            };
        }

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 在 Inspector 修改后保持通道、时长、强度与方向处于可发射范围。
        /// </summary>
        private void OnValidate()
        {
            if (channel == 0) channel = 1;
            duration = Mathf.Max(0.001f, duration);
            defaultAmplitude = Mathf.Max(0f, defaultAmplitude);
            if (defaultDirection.sqrMagnitude < 0.000001f) defaultDirection = Vector3.down;
        }

        #endregion
    }
}
