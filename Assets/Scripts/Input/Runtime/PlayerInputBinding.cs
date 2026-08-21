using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPG.PlayerInputSystem
{
    /// <summary>把一个 Input Action 映射到请求类型，并允许独立覆盖两阶段缓冲时间。</summary>
    [Serializable]
    public sealed class PlayerInputBinding
    {
        #region 序列化配置
        [SerializeField] private InputActionReference action;
        [SerializeField] private string actionName = string.Empty;
        [SerializeField] private PlayerInputType inputType;
        [SerializeField, Min(0f)] private float pressBufferDuration = 0.2f;
        [SerializeField, Min(0f)] private float releaseBufferDuration = 0.1f;
        #endregion

        #region 属性
        /// <summary>获取被监听的 Input Action。</summary>
        public InputActionReference Action => action;
        /// <summary>获取在 Action Reference 缺失时用于从同一输入资产解析的动作名称。</summary>
        public string ActionName => actionName;
        /// <summary>获取请求类型。</summary>
        public PlayerInputType InputType => inputType;
        #endregion

        #region 配置解析
        /// <summary>解析该绑定最终使用的 Press Buffer。</summary>
        public float ResolvePressDuration(float defaultDuration) => pressBufferDuration == 0f ? defaultDuration : pressBufferDuration;

        /// <summary>解析该绑定最终使用的 Release Buffer。</summary>
        public float ResolveReleaseDuration(float defaultDuration) => releaseBufferDuration == 0f ? defaultDuration : releaseBufferDuration;
        #endregion
    }
}
