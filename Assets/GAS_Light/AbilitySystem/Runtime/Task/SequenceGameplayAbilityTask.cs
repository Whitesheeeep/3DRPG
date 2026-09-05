using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>顺序推进子 Task，并安全处理子 Task 在 Start 内同步完成的情况。</summary>
    public sealed class SequenceGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段
        private readonly List<GameplayAbilityTask> children = new();
        private int currentIndex;
        private bool advancing;
        #endregion

        #region 构造
        /// <summary>从 Config 列表创建一次运行所需的全部独立子 Task。</summary>
        public SequenceGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            IReadOnlyList<GameplayAbilityTaskConfig> definitions)
            : base(runtime)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                GameplayAbilityTaskConfig config = definitions[i] ??
                    throw new InvalidOperationException("Sequence 不能创建空子 Task。");
                children.Add(config.CreateTaskInstance(runtime));
            }
        }
        #endregion

        #region 生命周期
        // 启动第一个子项；空 Sequence 会立即完成。
        protected override void OnStart() => Advance();

        // 正常提前结束只停止当前仍在运行的子项。
        protected override void OnStop() => StopCurrent();

        // 中断只取消当前仍在运行的子项。
        protected override void OnCancel() => CancelCurrent();

        /// <summary>将普通更新阶段转发给当前 Running 子 Task。</summary>
        /// <param name="deltaTime">普通更新阶段的秒数。</param>
        protected override void OnTick(float deltaTime) => GetCurrentRunningTask()?.Tick(deltaTime);

        /// <summary>将固定更新阶段转发给当前 Running 子 Task。</summary>
        /// <param name="fixedDeltaTime">固定更新阶段的秒数。</param>
        protected override void OnFixedTick(float fixedDeltaTime) =>
            GetCurrentRunningTask()?.FixedTick(fixedDeltaTime);

        /// <summary>将延迟更新阶段转发给当前 Running 子 Task。</summary>
        /// <param name="deltaTime">延迟更新阶段使用的秒数。</param>
        protected override void OnLateTick(float deltaTime) =>
            GetCurrentRunningTask()?.LateTick(deltaTime);

        /// <summary>将动画根运动阶段转发给当前 Running 子 Task。</summary>
        protected override void OnUpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation) =>
            GetCurrentRunningTask()?.UpdateAnimationMove(deltaPosition, deltaRotation);
        #endregion

        #region 推进
        // 循环推进同步完成项；advancing 防止 Completed 回调递归进入。
        private void Advance()
        {
            if (advancing) return;
            advancing = true;
            while (State == GameplayAbilityTaskState.Running)
            {
                if (currentIndex >= children.Count)
                {
                    Complete();
                    break;
                }

                GameplayAbilityTask child = children[currentIndex];
                child.Completed += OnChildCompleted;
                child.Start();
                if (child.State == GameplayAbilityTaskState.Running) break;
                child.Completed -= OnChildCompleted;
                if (child.State != GameplayAbilityTaskState.Completed) break;
                currentIndex++;
            }
            advancing = false;
        }

        // 异步子项完成时推进；同步完成由 Advance 当前栈直接处理。
        private void OnChildCompleted(GameplayAbilityTask child)
        {
            child.Completed -= OnChildCompleted;
            if (advancing) return;
            currentIndex++;
            Advance();
        }

        // 解除完成回调后停止当前运行项，避免终止阶段继续推进。
        private void StopCurrent()
        {
            if (currentIndex >= children.Count) return;
            GameplayAbilityTask child = children[currentIndex];
            child.Completed -= OnChildCompleted;
            child.Stop();
        }

        // 解除完成回调后取消当前运行项，避免中断阶段继续推进。
        private void CancelCurrent()
        {
            if (currentIndex >= children.Count) return;
            GameplayAbilityTask child = children[currentIndex];
            child.Completed -= OnChildCompleted;
            child.Cancel();
        }

        /// <summary>获取当前仍在运行的子 Task。</summary>
        /// <returns>存在 Running 子项时返回该 Task，否则返回 null。</returns>
        private GameplayAbilityTask GetCurrentRunningTask()
        {
            if (currentIndex >= children.Count) return null;
            GameplayAbilityTask child = children[currentIndex];
            return child.State == GameplayAbilityTaskState.Running ? child : null;
        }
        #endregion
    }
}
