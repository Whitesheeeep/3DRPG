using RPG.Character.State;
using UnityEngine;

namespace RPG.PlayerInputSystem
{
    /// <summary>把输入控制器的连续 Move 状态转换为镜头相对的世界水平移动意图。</summary>
    public sealed class MoveInputIntentArbiter : GameplayInputIntentArbiter
    {
        #region 仲裁

        /// <summary>
        /// 读取本帧连续 MoveInput，并将其转换为世界 X/Z 平面方向写入 Blackboard。
        /// 该步骤只记录玩家输入事实，不判断当前角色是否能够执行移动。
        /// </summary>
        /// <param name="inputController">提供当前连续 MoveInput 的输入控制器。</param>
        /// <param name="blackboard">接收世界水平移动意图的稳定玩家黑板。</param>
        /// <param name="cameraTransform">用于确定水平前方和右方的镜头 Transform，可为空。</param>
        protected internal override void ArbitrateFrame(
            PlayerInputController inputController,
            PlayerStateBlackboard blackboard,
            Transform cameraTransform)
        {
            // MoveInput 是持续状态，不创建或消费离散 InputRequest Handle。
            blackboard.MoveWorldInput = ConvertToCameraRelativeWorld(inputController.MoveInput, cameraTransform);
        }

        /// <summary>把二维输入转换为保留输入强度的世界水平向量。</summary>
        /// <param name="input">输入系统采样的 X/Y 轴值。</param>
        /// <param name="cameraTransform">用于确定镜头朝向的 Transform，可为空。</param>
        /// <returns>世界 X/Z 平面中的连续移动意图。</returns>
        private static Vector3 ConvertToCameraRelativeWorld(Vector2 input, Transform cameraTransform)
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
