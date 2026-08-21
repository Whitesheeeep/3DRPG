using System;
using System.Collections.Generic;
using RPG.Character.State;
using RPG.InteractionSystem;
using WS_Modules.GAS.TAG;

namespace RPG.PlayerInputSystem
{
    /// <summary>按注册顺序统一执行输入仲裁策略，并把首个命中的结果发布为帧级 Intent。</summary>
    public sealed class GameplayInputIntentArbiterManager
    {
        #region 字段与属性
        // 依赖
        private readonly PlayerInputController inputController;
        private readonly PlayerStateBlackboard blackboard;

        // 仲裁管线的注册表与状态，按照注册顺序执行策略，首个命中策略的 Intent 会被发布到黑板。
        private readonly List<GameplayInputIntentArbiter> arbiters = new();
        private InteractionInputIntentArbiter interactionInputIntentArbiter;

        /// <summary>获取当前已注册仲裁策略的只读顺序视图。</summary>
        public IReadOnlyList<GameplayInputIntentArbiter> Arbiters => arbiters;
        #endregion

        #region 事件
        /// <summary>报告仲裁器尝试发布 Intent 的结果，供调试面板记录失败原因和来源句柄。</summary>
        internal event Action<GameplayTag, InputRequestHandle, bool> IntentPublicationReported;
        #endregion

        #region 构造
        /// <summary>创建读取输入 Request 并向玩家黑板发布 Intent 的仲裁管理器。</summary>
        /// <param name="inputController">提供当前只读 Request 顺序视图的输入 Controller。</param>
        /// <param name="blackboard">提供仲裁状态查询与帧级 Intent 发布的玩家黑板。</param>
        public GameplayInputIntentArbiterManager(PlayerInputController inputController,
            PlayerStateBlackboard blackboard)
        {
            this.inputController = inputController ?? throw new ArgumentNullException(nameof(inputController));
            this.blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        #endregion

        #region 注册与仲裁
        /// <summary>注册项目约定的默认仲裁策略。</summary>
        public void RegisterDefaultArbiters()
        {
            interactionInputIntentArbiter ??= new InteractionInputIntentArbiter();
            RegisterArbiter(interactionInputIntentArbiter);
        }

        /// <summary>清除当前全部仲裁策略注册。</summary>
        public void UnregisterAllArbiters()
        {
            arbiters.Clear();
        }

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
        public bool UnregisterArbiter(GameplayInputIntentArbiter arbiter)
        {
            return arbiter != null && arbiters.Remove(arbiter);
        }

        /// <summary>对当前 Request 快照执行一帧仲裁，每个阶段采用首个命中的注册策略。</summary>
        public void ArbitrateFrame()
        {
            for (int i = 0; i < inputController.Requests.Count; i++)
            {
                IReadOnlyPlayerInputRequest request = inputController.Requests[i];
                if (request.HasBufferedPress)
                    TryPublishFirstMatch(request, PlayerInputRequestStage.Press, request.PressHandle);
                if (request.HasBufferedRelease)
                    TryPublishFirstMatch(request, PlayerInputRequestStage.Release, request.ReleaseHandle);
            }
        }

        /// <summary>按注册顺序发布首个成功策略的 Intent。</summary>
        private void TryPublishFirstMatch(IReadOnlyPlayerInputRequest request, PlayerInputRequestStage stage,
            InputRequestHandle sourceHandle)
        {
            for (int i = 0; i < arbiters.Count; i++)
            {
                if (!arbiters[i].ResolveIntent(request, stage, blackboard, out GameplayTag intentTag)) continue;
                // 只有黑板真正接收了 Tag 和来源句柄，当前仲裁器才拥有本阶段的胜出权。
                bool published = blackboard.PublishFrameIntent(intentTag, sourceHandle);
                IntentPublicationReported?.Invoke(intentTag, sourceHandle, published);
                if (published) return;
            }
        }

        #endregion
    }
}
