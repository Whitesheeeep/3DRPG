#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>将 GameplayAbilityData 的双击入口路由到 GAS 主窗口的 GA 页面。</summary>
    public static class GameplayAbilityAssetOpenHandler
    {
        // 仅消费 GameplayAbilityData 的打开事件，其他资源继续走 Unity 默认行为。
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is not GameplayAbilityData ability)
                return false;
            GAS_SettingWindow.ShowGameplayAbility(ability);
            return true;
        }
    }
}
#endif
