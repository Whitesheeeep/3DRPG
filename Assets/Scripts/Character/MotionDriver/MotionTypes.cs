using System;

namespace RPG.Character
{
    /// <summary>定义可以独立竞争的角色运动通道。</summary>
    [Flags]
    public enum MotionChannels
    {
        /// <summary>不控制任何运动通道。</summary>
        None = 0,
        /// <summary>控制世界空间水平 X/Z 位移。</summary>
        Horizontal = 1 << 0,
        /// <summary>控制世界空间垂直 Y 位移。</summary>
        Vertical = 1 << 1,
        /// <summary>控制 CharacterRoot 旋转。</summary>
        Rotation = 1 << 2
    }

    /// <summary>定义项目约定的运动控制优先级段。</summary>
    public enum MotionPriority
    {
        /// <summary>重力等基础垂直运动。</summary>
        Gravity = 0,
        /// <summary>普通 Locomotion 状态机。</summary>
        Locomotion = 100,
        /// <summary>Gameplay Ability 技能运动。</summary>
        Skill = 200,
        /// <summary>击退、吸附等强制运动。</summary>
        ForcedMotion = 300
    }
}
