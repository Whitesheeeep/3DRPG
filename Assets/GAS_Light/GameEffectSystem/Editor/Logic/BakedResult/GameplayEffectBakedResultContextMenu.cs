#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.Baking.Editor;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>为 GameplayEffectData Inspector 提供 Curve 烘焙结果入口。</summary>
    internal static class GameplayEffectBakedResultContextMenu
    {
        /// <summary>从 GameplayEffectData Inspector 打开烘焙结果。</summary>
        /// <param name="command">包含当前 GEData 的菜单命令。</param>
        [MenuItem("CONTEXT/GameplayEffectData/查看烘焙结果")]
        private static void Open(MenuCommand command) => BakedResultViewerWindow.Open((GameplayEffectData)command.context);
    }
}
#endif
