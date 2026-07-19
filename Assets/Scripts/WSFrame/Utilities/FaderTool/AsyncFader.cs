using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 异步数值渐变工具
    /// </summary>
    public static class AsyncFader
    {
        /// <summary>
        /// 对浮点数进行异步渐变
        /// </summary>
        /// <param name="startValue">起始值</param>
        /// <param name="endValue">目标值</param>
        /// <param name="duration">持续时间(秒)</param>
        /// <param name="onUpdate">更新回调，用于设置目标属性</param>
        /// <param name="token">取消令牌</param>
        /// <param name="curve">自定义曲线 (优先级高于 useSmoothStep)</param>
        /// <param name="useSmoothStep">是否使用平滑插值 (仅当 curve 为 null 时生效)</param>
        public static async UniTask FadeFloatAsync(
            float startValue, 
            float endValue, 
            float duration, 
            Action<float> onUpdate, 
            CancellationToken token = default,
            AnimationCurve curve = null,
            bool useSmoothStep = false)
        {
            float timer = 0f;
            
            // 立即设置初始值
            onUpdate?.Invoke(startValue);

            while (timer < duration)
            {
                // 检查取消
                if (token.IsCancellationRequested) return;

                // 等待下一帧
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                
                timer += Time.deltaTime;
                float progress = Mathf.Clamp01(timer / duration);

                // 计算评估所需的 t 值 (0~1)
                float t = progress;

                if (curve != null)
                {
                    // 使用曲线评估进度
                    t = curve.Evaluate(progress);
                }
                else if (useSmoothStep)
                {
                    t = Mathf.SmoothStep(0f, 1f, progress);
                }

                // 数值插值
                float current = Mathf.LerpUnclamped(startValue, endValue, t);

                // 回调每一帧的数值
                onUpdate?.Invoke(current);
            }

            // 确保结束时数值精确
            if (!token.IsCancellationRequested)
            {
                onUpdate?.Invoke(endValue);
            }
        }
    }
}


