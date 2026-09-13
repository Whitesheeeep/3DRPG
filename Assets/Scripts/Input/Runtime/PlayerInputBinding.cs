using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPG.PlayerInputSystem
{
    /// <summary>定义 InputAction 触发后交付给上层的方式。</summary>
    public enum PlayerInputDeliveryMode
    {
        /// <summary>创建可被玩法仲裁器读取和消费的输入 Request。</summary>
        BufferedRequest = 0,
        /// <summary>在 performed 回调中立即通知订阅者，不创建输入 Request。</summary>
        ImmediateNotification = 1
    }

    /// <summary>把一个 Input Action 映射到输入类型，并配置其缓冲或即时交付方式。</summary>
    [Serializable]
    public sealed class PlayerInputBinding
    {
        #region 序列化配置
        [SerializeField] private InputActionReference action;
        [SerializeField] private string actionName = string.Empty;
        [SerializeField] private PlayerInputType inputType;
        [SerializeField] private float pressBufferDuration = 0.2f;
        [SerializeField] private float releaseBufferDuration = 0.1f;
        [SerializeField] private PlayerInputDeliveryMode deliveryMode;
        #endregion

        #region 属性
        /// <summary>获取被监听的 Input Action。</summary>
        public InputActionReference Action => action;
        /// <summary>获取在 Action Reference 缺失时用于从同一输入资产解析的动作名称。</summary>
        public string ActionName => actionName;
        /// <summary>获取请求类型。</summary>
        public PlayerInputType InputType => inputType;
        /// <summary>获取 InputAction 触发后的交付方式。</summary>
        public PlayerInputDeliveryMode DeliveryMode => deliveryMode;
        #endregion

        #region 配置解析
        /// <summary>解析该绑定最终使用的 Press Buffer。</summary>
        public float ResolvePressDuration(float defaultDuration) => pressBufferDuration == 0f ? defaultDuration : pressBufferDuration;

        /// <summary>解析该绑定最终使用的 Release Buffer。</summary>
        public float ResolveReleaseDuration(float defaultDuration) => releaseBufferDuration == 0f ? defaultDuration : releaseBufferDuration;
        #endregion
    }
}
