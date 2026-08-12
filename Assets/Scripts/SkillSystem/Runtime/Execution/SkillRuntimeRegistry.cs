using System.Collections.Generic;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 定义单次技能执行所需的固定轨道处理管线，并为每次执行创建独立处理器实例。
    /// </summary>
    internal static class SkillRuntimeRegistry
    {
        #region 创建

        /// <summary>
        /// 创建内置轨道的默认运行时注册表。
        /// </summary>
        /// <returns>描述内置轨道处理顺序的运行时注册表。</returns>
        /*public static SkillRuntimeRegistry CreateDefault()
        {
            return new SkillRuntimeRegistry();
        }
        */

        #endregion

        #region 处理管线

        /// <summary>
        /// 按固定类型顺序为一次技能执行创建完整且相互独立的处理器管线。
        /// </summary>
        /// <returns>本次执行独占的六类轨道处理器。</returns>
        public static IReadOnlyList<ISkillTrackRuntimeHandler> CreateHandlers()
        {
            // 不缓存有状态 Handler，确保不同角色与连续技能执行之间完全隔离。
            return new ISkillTrackRuntimeHandler[]
            {
                new ActionPhaseRuntimeHandler(),
                new AnimationRuntimeHandler(),
                new AttackDetectionRuntimeHandler(),
                new VfxRuntimeHandler(),
                new AudioRuntimeHandler(),
                new EventRuntimeHandler()
            };
        }

        #endregion
    }
}
