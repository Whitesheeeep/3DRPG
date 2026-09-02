using System;
using System.Collections.Generic;
using RPG.Character.State;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace RPG.PlayerInputSystem
{
    /// <summary>统一仲裁离散输入 Request 与连续移动输入，并把最终意图写入玩家黑板。</summary>
    public sealed class GameplayInputIntentArbiterManager
    {
        #region 字段与属性
        // 依赖
        private readonly PlayerInputController inputController;
        private readonly PlayerStateBlackboard blackboard;

        // 仲裁管线的注册表与状态，按照注册顺序执行策略，首个命中策略的 Intent 会被发布到黑板。
        private readonly List<GameplayInputIntentArbiter> arbiters = new();

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
        /// <summary>
        /// 保留默认注册入口以兼容旧启动代码；当前选项窗口由 Unity EventSystem 导航，
        /// 因此不再注册 InteractionInputIntentArbiter。
        /// </summary>
        public void RegisterDefaultArbiters()
        {
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

        /// <summary>对离散 Request 和连续移动采样执行一帧统一仲裁。</summary>
        /// <param name="cameraTransform">提供水平前方与右方的当前镜头基准；为空时使用世界 X/Z。</param>
        public void ArbitrateFrame(Transform cameraTransform)
        {
            // 离散输入先按策略顺序发布 IntentTag；它与连续 Move 共用同一个帧级仲裁入口。
            for (int i = 0; i < inputController.Requests.Count; i++)
            {
                IReadOnlyPlayerInputRequest request = inputController.Requests[i];
                if (request.HasBufferedPress)
                    TryPublishFirstMatch(request, PlayerInputRequestStage.Press, request.PressHandle);
                if (request.HasBufferedRelease)
                    TryPublishFirstMatch(request, PlayerInputRequestStage.Release, request.ReleaseHandle);
            }

            // Move 只记录 Player 当前真实输入，不读取 GameplayTag，也不判断场景或控制锁。
            // 是否允许当前角色响应由 PlayerController/Locomotion 业务层决定，最终运动阻断仍由 MotionDriver 处理。
            blackboard.MoveWorldInput = ConvertMoveToCameraRelativeWorld(inputController.MoveInput, cameraTransform);
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

        /// <summary>把输入平面向量转换为保留输入强度的镜头相对世界水平向量。</summary>
        /// <param name="input">Input System 采样到的 X/Y 连续输入。</param>
        /// <param name="cameraTransform">用于确定水平前方与右方的镜头 Transform。</param>
        /// <returns>世界 X/Z 平面中的连续移动意图。</returns>
        private static Vector3 ConvertMoveToCameraRelativeWorld(Vector2 input, Transform cameraTransform)
        {
            if (cameraTransform == null) return new Vector3(input.x, 0f, input.y);

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return right * input.x + forward * input.y;
        }

        #endregion
    }
}
