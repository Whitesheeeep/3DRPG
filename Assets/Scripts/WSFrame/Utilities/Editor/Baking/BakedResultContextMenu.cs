#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Baking.Editor
{
    /// <summary>为所有实现烘焙结果接口的 Project 资产提供通用右键入口。</summary>
    internal static class BakedResultContextMenu
    {
        private const string MenuPath = "Assets/RPG/查看烘焙结果";

        /// <summary>打开当前选中资产的烘焙结果窗口。</summary>
        private static void OpenSelectedAsset()
        {
            if (Selection.objects.Length != 1 || Selection.activeObject is not IBakedResultDataSource source) return;
            BakedResultViewerWindow.Open(source);
        }

        /// <summary>判断通用烘焙结果菜单当前是否可用。</summary>
        /// <returns>单选接口数据源资产时返回 true。</returns>
        private static bool ValidateSelectedAsset()
        {
            return Selection.objects.Length == 1 && Selection.activeObject is IBakedResultDataSource;
        }

        [MenuItem(MenuPath, false, 2050)]
        private static void OpenSelectedAssetMenu() => OpenSelectedAsset();

        [MenuItem(MenuPath, true, 2050)]
        private static bool ValidateSelectedAssetMenu() => ValidateSelectedAsset();
    }
}
#endif
