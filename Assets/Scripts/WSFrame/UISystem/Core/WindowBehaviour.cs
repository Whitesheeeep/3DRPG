using System;
using UnityEngine;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI Window 基类行为，所有窗口都需要继承这个类，来实现自己的UI逻辑，管理窗口的生命周期，窗口的显示和隐藏等功能
    /// </summary>
    public abstract class WindowBehaviour
    {
        public GameObject GameObject { get; set; } //当前窗口物体
        public Transform Transform { get; set; } //代表自己
        public Canvas Canvas { get; set; }
        public string Name { get; set; }
        public bool Visible { get; set; }
        /// <summary>
        /// 是否是通过堆栈系统弹出的弹窗
        /// </summary>
        public bool PopStack { get; set; }//是否是通过堆栈系统弹出的弹窗
        /// <summary>
        /// 全屏窗口标志(在窗口Awake接口中进行设置,智能显隐开启后当全屏弹窗弹出时，被遮挡的窗口都会通过伪隐藏隐藏掉，从而提升性能)
        /// </summary>
        public bool FullScreenWindow { get; set; }

        public Action<WindowBase> PopStackListener { get; set; }

        public virtual void OnAwake() { } //只会在物体创建时执行一次 ，与Mono Awake调用时机和次数保持一致
        public virtual void OnShow() { }  //在物体显示时执行一次，与MonoOnEnable一致
        public virtual void OnUpdate() { }
        public virtual void OnHide() { } //在物体隐藏时执行一次，与Mono OnDisable 一致
        public virtual void OnDestroy() { } //在当前界面被销毁时调用一次

        public virtual void SetVisible(bool isVisble) { }  //设置物体的可见性
    }
}