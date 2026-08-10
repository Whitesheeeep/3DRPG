#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.Editor
{
    /// <summary>把 Cue Database 和 CueData 的双击行为路由到 GAS 主窗口 Cue 页面。</summary>
    public static class GameplayCueAssetOpenHandler
    {
        /// <summary>处理 Project 窗口双击的 Cue 资产。</summary>
        /// <param name="instanceId">Unity 对象实例 ID。</param>
        /// <param name="line">脚本行号，占位参数。</param>
        /// <returns>当前资产由 Cue Editor 处理时返回 true。</returns>
        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            UnityEngine.Object asset = EditorUtility.InstanceIDToObject(instanceId);
            if (asset is GameplayCueDatabase database)
            {
                GAS_SettingWindow.ShowGameplayCues(database);
                return true;
            }

            if (asset is GameplayCueData cue)
            {
                GAS_SettingWindow.ShowGameplayCue(cue);
                return true;
            }

            return false;
        }
    }
}
#endif
