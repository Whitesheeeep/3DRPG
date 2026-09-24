using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPG.PlayerInputSystem
{
    #region 交付模式

    /// <summary>定义 InputAction 触发后交付给上层的方式。</summary>
    public enum PlayerInputDeliveryMode
    {
        /// <summary>创建可被玩法查询和消费的输入 Request。</summary>
        BufferedRequest = 0,
        /// <summary>在 performed 回调中立即通知订阅者，不创建输入 Request。</summary>
        ImmediateNotification = 1
    }

    #endregion

    /// <summary>保存一个 InputAction 到离散 Request 的完整缓冲配置。</summary>
    [CreateAssetMenu(fileName = "PlayerInputBinding", menuName = "RPG/Input/Player Input Binding")]
    public sealed class PlayerInputBinding : ScriptableObject
    {
        #region 配置字段

        [SerializeField, LabelText("Input Action")]
        private InputActionReference action;
        [SerializeField, LabelText("输入类型")]
        private PlayerInputType inputType;
        [SerializeField, MinValue(0f), LabelText("Press 缓冲秒数")]
        private float pressBufferDuration = 0.2f;
        [SerializeField, MinValue(0f), LabelText("Release 缓冲秒数")]
        private float releaseBufferDuration = 0.1f;
        [SerializeField, MinValue(0f), LabelText("Click 最大按住秒数")]
        private float clickMaxHeldDuration = 0.2f;
        [SerializeField, MinValue(0f), LabelText("Click 缓冲秒数")]
        private float clickBufferDuration = 0.2f;
        [SerializeField, LabelText("交付方式")]
        private PlayerInputDeliveryMode deliveryMode;

        #endregion

        #region 属性

        /// <summary>获取被监听的 InputAction。</summary>
        public InputActionReference Action => action;
        /// <summary>获取 Request 使用的逻辑输入类型。</summary>
        public PlayerInputType InputType => inputType;
        /// <summary>获取 Press 阶段的缓冲时长。</summary>
        public float PressBufferDuration => pressBufferDuration;
        /// <summary>获取 Release 阶段的缓冲时长。</summary>
        public float ReleaseBufferDuration => releaseBufferDuration;
        /// <summary>获取区分 Click 与持续按住的最长时长。</summary>
        public float ClickMaxHeldDuration => clickMaxHeldDuration;
        /// <summary>获取 Click 阶段的缓冲时长。</summary>
        public float ClickBufferDuration => clickBufferDuration;
        /// <summary>获取 InputAction 触发后的交付方式。</summary>
        public PlayerInputDeliveryMode DeliveryMode => deliveryMode;

        #endregion

        #region Editor 操作

        /// <summary>在 Inspector 执行 Reset 时仅恢复时间参数，保留已经配置的输入身份。</summary>
        private void Reset()
        {
            pressBufferDuration = 0.2f;
            releaseBufferDuration = 0.1f;
            clickMaxHeldDuration = 0.2f;
            clickBufferDuration = 0.2f;
        }

        #endregion
    }
}
