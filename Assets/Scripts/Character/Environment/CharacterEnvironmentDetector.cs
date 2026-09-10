using System;
using RPG.Character.State;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// 统一编排 CharacterRoot 侧的环境检测器。
    /// 当前只包含 Locomotion 环境检测；后续攀爬、贴墙和边缘检测应作为同级子检测器加入这里。
    /// </summary>
    [Serializable]
    public sealed class CharacterEnvironmentDetector
    {
        #region 依赖字段

        // 子检测器按固定顺序执行，PlayerController 不直接依赖具体检测类型。
        [SerializeField]
        private LocomotionEnvironmentDetector locomotionDetector = new();

        #endregion

        #region 运行时状态

        private bool initialized;

        #endregion

        #region 生命周期

        /// <summary>
        /// 绑定 CharacterRoot 并让子检测器建立初始化环境快照。
        /// 该快照只设置采样基线，不推进角色状态或移动。
        /// </summary>
        /// <param name="characterRoot">承载共享碰撞与角色队伍的根节点。</param>
        /// <param name="blackboard">接收初始环境事实的共享黑板。</param>
        public void Initialize(Transform characterRoot, PlayerStateBlackboard blackboard)
        {
            if (locomotionDetector == null)
                throw new InvalidOperationException("CharacterEnvironmentDetector 缺少 LocomotionEnvironmentDetector 配置。");
            locomotionDetector.Initialize(characterRoot, blackboard);
            initialized = true;
        }

        /// <summary>
        /// 在 PlayerController.Update 开始阶段按固定顺序推进全部环境子检测器。
        /// 检测器只写入 Blackboard，不决定状态、不提交 MotionDriver 请求、不调用 CharacterController.Move。
        /// </summary>
        /// <param name="deltaTime">当前渲染帧时间步长。</param>
        /// <param name="blackboard">稳定 Player 共享的状态黑板。</param>
        public void TickUpdate(float deltaTime, PlayerStateBlackboard blackboard)
        {
            EnsureInitialized();
            if (blackboard == null)
                throw new ArgumentNullException(nameof(blackboard));

            // 后续 ClimbingEnvironmentDetector 应在这里按约定顺序执行，PlayerController 不增加新的调用分支。
            locomotionDetector.TickUpdate(deltaTime, blackboard);
        }

        /// <summary>转发当前环境子检测器的 Gizmo 绘制。</summary>
        public void OnGizmosDraw(Transform characterRootForEditor)
        {
            if (characterRootForEditor == null)
                throw new ArgumentNullException(nameof(characterRootForEditor));
            locomotionDetector?.OnGizmosDraw(characterRootForEditor);
        }
        #endregion

        #region 校验

        /// <summary>确认总检测器已经绑定所有必要的 CharacterRoot 依赖。</summary>
        private void EnsureInitialized()
        {
            if (!initialized)
                throw new InvalidOperationException("CharacterEnvironmentDetector 尚未初始化。");
        }

        #endregion
    }
}
