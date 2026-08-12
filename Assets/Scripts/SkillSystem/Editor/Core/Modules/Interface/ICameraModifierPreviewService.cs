#if UNITY_EDITOR
namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 定义 Camera Modifier Inspector 草稿与 Scene View Preview 的隔离通信边界。
    /// </summary>
    internal interface ICameraModifierPreviewService
    {
        /// <summary>设置指定 Clip 的独立预览草稿，不修改 SkillConfig。</summary>
        void SetDraft(string clipId, CameraModifierDataBase data);
        /// <summary>清除指定 Clip 草稿并恢复权威 Config 预览。</summary>
        void ClearDraft(string clipId);
    }
}
#endif
