#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>将 GameplayEffectData 双击入口统一路由到 GAS 主窗口 GE 模块。</summary>
    public static class GameplayEffectAssetOpenHandler
    {
        // Project 窗口双击 GE 时打开内嵌页面，其他资产继续使用 Unity 默认行为。
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is not GameplayEffectData effect)
                return false;
            GAS_SettingWindow.ShowGameplayEffect(effect);
            return true;
        }
    }
}
#endif
