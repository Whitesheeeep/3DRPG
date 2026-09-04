using System;
using System.Collections.Generic;
using RPG.Character.State;
using UnityEngine;

namespace RPG.PlayerInputSystem
{
    /// <summary>统一调度输入意图分析策略，并把共享输入数据交给各个 Arbiter。</summary>
    public sealed class GameplayInputIntentArbiterManager
    {
        #region 字段与属性

        // 依赖：Manager 只读取输入快照，并将稳定 Blackboard 传给各个策略。
        private readonly PlayerInputController inputController;
        private readonly PlayerStateBlackboard blackboard;
        private readonly List<GameplayInputIntentArbiter> arbiters = new();

        /// <summary>获取当前已注册仲裁策略的只读顺序视图。</summary>
        public IReadOnlyList<GameplayInputIntentArbiter> Arbiters => arbiters;

        #endregion

        #region 构造

        /// <summary>创建读取输入快照并调度意图分析策略的管理器。</summary>
        /// <param name="inputController">提供连续输入和离散 Request 的输入控制器。</param>
        /// <param name="blackboard">接收各个策略分析结果的稳定玩家黑板。</param>
        public GameplayInputIntentArbiterManager(
            PlayerInputController inputController,
            PlayerStateBlackboard blackboard)
        {
            this.inputController = inputController ?? throw new ArgumentNullException(nameof(inputController));
            this.blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        #endregion

        #region 注册与仲裁

        /// <summary>
        /// 注册运行时默认输入策略。默认策略只分析输入，不读取业务 Tag 或玩家锁定状态。
        /// 重复调用不会重复添加策略。
        /// </summary>
        public void RegisterDefaultArbiters()
        {
            RegisterArbiterIfMissing(new MoveInputIntentArbiter());
        }

        /// <summary>清除当前全部仲裁策略注册。</summary>
        public void UnregisterAllArbiters() => arbiters.Clear();

        /// <summary>把策略追加到仲裁顺序末尾。</summary>
        /// <param name="arbiter">待注册策略。</param>
        /// <returns>首次注册成功时返回 true。</returns>
        public bool RegisterArbiter(GameplayInputIntentArbiter arbiter)
        {
            if (arbiter == null) throw new ArgumentNullException(nameof(arbiter));
            if (arbiters.Contains(arbiter)) return false;
            arbiters.Add(arbiter);
            return true;
        }

        /// <summary>从仲裁顺序中移除指定策略。</summary>
        /// <param name="arbiter">待注销策略。</param>
        /// <returns>找到并移除策略时返回 true。</returns>
        public bool UnregisterArbiter(GameplayInputIntentArbiter arbiter) =>
            arbiter != null && arbiters.Remove(arbiter);

        /// <summary>按注册顺序执行一帧输入意图分析。</summary>
        /// <param name="cameraTransform">提供镜头水平基准的 Transform，可为空。</param>
        public void ArbitrateFrame(Transform cameraTransform)
        {
            // Manager 只控制策略时序；连续输入和离散 Request 的具体分析全部由 Arbiter 实现。
            for (int i = 0; i < arbiters.Count; i++)
                arbiters[i].ArbitrateFrame(inputController, blackboard, cameraTransform);
        }

        /// <summary>仅在默认策略尚未注册时追加一个策略实例。</summary>
        /// <param name="arbiter">需要追加的默认策略。</param>
        private void RegisterArbiterIfMissing(GameplayInputIntentArbiter arbiter)
        {
            for (int i = 0; i < arbiters.Count; i++)
                if (arbiters[i].GetType() == arbiter.GetType()) return;
            arbiters.Add(arbiter);
        }

        #endregion
    }
}
