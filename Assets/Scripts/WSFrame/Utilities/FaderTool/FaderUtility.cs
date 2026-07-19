using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 状态型数值渐变器
    /// 优势：
    /// 1. 自动管理 CancellationToken，防止多个动画冲突
    /// 2. 记住当前值，支持从任意当前状态继续渐变
    /// 3. 支持配置复用
    /// </summary>
    [Serializable]
    public class FaderUtility
    {
        // 状态
        private float _currentValue;
        private CancellationTokenSource _cts;

        // 配置 (可以在 Inspector 中序列化配置)
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
        public bool useUnscaledTime = false;

        public float Value
        {
            get => _currentValue;
            set => _currentValue = value;
        }

        public FaderUtility(float initialValue = 0f)
        {
            _currentValue = initialValue;
        }

        /// <summary>
        /// 从指定值渐变到目标值（自动打断上一个渐变）
        /// </summary>
        public async UniTask FromTo(float startValue, float targetValue, float duration, Action<float> onUpdate = null)
        {
            // 立即设置当前值
            _currentValue = startValue;

            // 复用 To 方法
            await To(targetValue, duration, onUpdate);
        }

        /// <summary>
        /// 渐变到目标值（自动打断上一个渐变）
        /// </summary>
        public async UniTask To(float targetValue, float duration, Action<float> onUpdate = null)
        {
            // 打断上一次任务
            Kill();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            float startValue = _currentValue;
            float timer = 0f;

            while (timer < duration)
            {
                if (token.IsCancellationRequested)
                {
                    DisposeCts();
                    return;
                }

                // 每一帧在 Update 阶段继续
                // 优化：不把 token 传给 Yield，避免它抛出异常。我们自己在下面手动 check。
                await UniTask.Yield();

                // 手动检测取消，优雅退出而不抛异常
                if (token.IsCancellationRequested)
                {
                    DisposeCts();
                    return;
                }

                float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                timer += dt;

                float progress = Mathf.Clamp01(timer / duration);
                float t = curve != null ? curve.Evaluate(progress) : progress;

                // 更新内部值
                _currentValue = Mathf.LerpUnclamped(startValue, targetValue, t);

                // 触发外部回调
                onUpdate?.Invoke(_currentValue);
            }

            // 完成
            _currentValue = targetValue;
            onUpdate?.Invoke(_currentValue);

            DisposeCts();
        }

        /// <summary>
        /// 停止当前渐变
        /// </summary>
        public void Kill()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                DisposeCts();
            }
        }

        private void DisposeCts()
        {
            _cts?.Dispose();
            _cts = null;
        }

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
        /// <param name="useUnscaledTime">是否使用与游戏时间无关的时间间隙</param>
        public static async UniTask FadeFloatAsync(
            float startValue,
            float endValue,
            float duration,
            Action<float> onUpdate,
            CancellationToken token = default,
            AnimationCurve curve = null,
            bool useSmoothStep = false,
            bool useUnscaledTime = false)
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

                timer += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
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