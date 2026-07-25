#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>把 GameplayTagDatabase 的双击行为路由到专用编辑窗口。</summary>
    internal static class GameplayTagDatabaseOpenAsset
    {
        // 仅处理 GameplayTagDatabase，其他资产继续走 Unity 默认打开流程。
        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            GameplayTagDatabase database = EditorUtility.InstanceIDToObject(instanceId) as GameplayTagDatabase;
            if (database == null) return false;
            GAS_SettingWindow.ShowWindow(database);
            return true;
        }
    }
}
#endif
