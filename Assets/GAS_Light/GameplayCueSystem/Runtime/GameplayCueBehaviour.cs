using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>挂在 Cue 预制体上的表现行为基类，具体效果通过重写生命周期方法实现。</summary>
    public abstract class GameplayCueBehaviour : MonoBehaviour
    {
        #region 表现生命周期
        /// <summary>对象从对象池取出并完成位置设置后调用。</summary>
        protected virtual void OnCueSpawn(GameplayCueRuntime runtime) { }
        /// <summary>执行一次性表现。</summary>
        protected virtual void OnExecute(GameplayCueRuntime runtime) { }
        /// <summary>启动持续表现。</summary>
        protected virtual void OnActive(GameplayCueRuntime runtime) { }
        /// <summary>停止持续表现。</summary>
        protected virtual void OnRemove(GameplayCueRuntime runtime) { }
        /// <summary>对象即将归还对象池时重置内部状态。</summary>
        protected virtual void OnCueRecycle(GameplayCueRuntime runtime) { }
        #endregion

        #region 内部回调
        // 这些入口只由 GameplayCueCtrl 调用，集中处理表现脚本边界上的异常。
        internal void InvokeCueSpawn(GameplayCueRuntime runtime) => InvokeSafely(() => OnCueSpawn(runtime));
        internal void InvokeExecute(GameplayCueRuntime runtime) => InvokeSafely(() => OnExecute(runtime));
        internal void InvokeActive(GameplayCueRuntime runtime) => InvokeSafely(() => OnActive(runtime));
        internal void InvokeRemove(GameplayCueRuntime runtime) => InvokeSafely(() => OnRemove(runtime));

        /// <summary>
        /// 对象即将归还对象池时重置内部状态，表现脚本属于资源边界，记录异常后继续完成对象池回收，避免单个 Cue 卡住后续表现。
        /// </summary>
        /// <param name="runtime">Cue 运行时对象。</param>
        internal void InvokeCueRecycle(GameplayCueRuntime runtime) => InvokeSafely(() => OnCueRecycle(runtime));

        // 表现脚本属于资源边界，记录异常后继续完成对象池回收，避免单个 Cue 卡住后续表现。
        private void InvokeSafely(Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
        #endregion
    }
}
