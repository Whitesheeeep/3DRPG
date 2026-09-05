using RPG.Character.State;
using UnityEngine;

namespace RPG.PlayerInputSystem
{
    /// <summary>定义一个无 Unity 生命周期的输入意图分析策略。</summary>
    public abstract class GameplayInputIntentArbiter
    {
        /// <summary>
        /// 分析输入控制器当前帧的数据，并将本策略负责的结果写入玩家黑板。
        /// 子类只表达输入意图，不判断 ASC、GameplayTag 或业务是否允许执行。
        /// </summary>
        /// <param name="inputController">提供连续输入和离散 Request 的输入控制器。</param>
        /// <param name="blackboard">接收本帧输入分析结果的稳定玩家黑板。</param>
        /// <param name="cameraTransform">用于计算镜头相对方向的镜头 Transform，可为空。</param>
        protected internal abstract void ArbitrateFrame(
            PlayerInputController inputController,
            PlayerStateBlackboard blackboard,
            Transform cameraTransform);
    }
}
