#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>把 Registry 与 AttributeSet 双击入口统一路由到 GAS 主窗口 Attribute 模块。</summary>
    public static class GameplayAttributeAssetOpenHandler
    {
        // 双击支持的资产时打开对应 Attribute 子页面。
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            UnityEngine.Object asset = EditorUtility.InstanceIDToObject(instanceId);
            if (asset is GameplayAttributeRegistry registry)
            {
                GAS_SettingWindow.ShowGameplayAttributes(
                    registry,
                    null,
                    GameplayAttributeEditorPage.Specs);
                return true;
            }

            if (asset is GameplayAttributeSet set)
            {
                GAS_SettingWindow.ShowGameplayAttributes(
                    null,
                    set,
                    GameplayAttributeEditorPage.Sets);
                return true;
            }

            return false;
        }
    }
}
#endif
