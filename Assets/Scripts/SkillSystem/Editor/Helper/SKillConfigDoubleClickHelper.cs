using UnityEditor;
using UnityEditor.Callbacks;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 把 Project 窗口中的 SkillConfig 双击操作路由到技能时间轴编辑器。
    /// </summary>
    internal static class SkillConfigOpenAssetHandler
    {
        /// <summary>
        /// 仅拦截 SkillConfig；其他资产继续交由 Unity 默认打开流程处理。
        /// </summary>
        /// <param name="instanceId">Project 资产对应的 Unity Instance ID。</param>
        /// <param name="line">Unity 请求打开的行号；SkillConfig 不使用该值。</param>
        /// <returns>成功处理 SkillConfig 时返回 true，否则返回 false。</returns>
        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            SkillConfig config = EditorUtility.InstanceIDToObject(instanceId) as SkillConfig;
            if (config == null) return false;

            // 统一入口负责复用窗口，并处理首次创建时 CreateGUI 尚未执行的时序。
            TimelineWindow.Open(config);
            return true;
        }
    }
}
