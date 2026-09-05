#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.Baking.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>为武器和圣遗物 Definition 提供 Inspector 烘焙结果入口。</summary>
    internal static class ItemBakedResultContextMenu
    {
        /// <summary>从武器 Definition Inspector 打开烘焙结果。</summary>
        /// <param name="command">包含当前武器的菜单命令。</param>
        [MenuItem("CONTEXT/WeaponDefinition/查看烘焙结果")]
        private static void OpenWeapon(MenuCommand command) => BakedResultViewerWindow.Open((WeaponDefinition)command.context);

        /// <summary>从圣遗物 Definition Inspector 打开烘焙结果。</summary>
        /// <param name="command">包含当前圣遗物的菜单命令。</param>
        [MenuItem("CONTEXT/ArtifactDefinition/查看烘焙结果")]
        private static void OpenArtifact(MenuCommand command) => BakedResultViewerWindow.Open((ArtifactDefinition)command.context);
    }
}
#endif
