#if UNITY_EDITOR
using System;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存一次 Scene Handle 编辑提交的 Clip 标识和独立检测数据快照。
    /// </summary>
    internal readonly struct AttackDetectionSceneEditCommit
    {
        internal string ClipId { get; }
        internal AttackDetectionDataBase DetectionData { get; }

        // 创建不可变提交值；DetectionData 已由场景 Drawer 创建为独立实例。
        internal AttackDetectionSceneEditCommit(string clipId, AttackDetectionDataBase detectionData)
        {
            ClipId = clipId;
            DetectionData = detectionData;
        }
    }

    /// <summary>
    /// 隔离 Scene Handle 草稿与 ViewModel，使预览处理器只发送完成后的语义编辑结果。
    /// </summary>
    internal interface IAttackDetectionSceneEditService
    {
        event Action<AttackDetectionSceneEditCommit> EditCommitted;

        /// <summary>
        /// 设置当前可显示 Scene Handle 的攻击检测 Clip；空字符串表示没有可编辑目标。
        /// </summary>
        /// <param name="clipId">稳定 Clip GUID。</param>
        void SetSelectedClip(string clipId);
    }
}
#endif