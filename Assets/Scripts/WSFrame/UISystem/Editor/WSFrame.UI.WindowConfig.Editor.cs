using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.UIModule
{
    internal static class WindowConfigEditorUtility
    {
        [MenuItem("CONTEXT/WindowConfig/GetWindowConfig")]
        private static void GeneratorWindowConfig(MenuCommand menuCommand)
        {
            if (menuCommand.context is not WindowConfig windowConfig)
            {
                return;
            }

            var setting = WSFrameRoot.Instance?.FrameSetting ?? GetSetting();
            if (setting == null || setting.uiManagerSetting?.WindowPrefabFolderPathArr == null)
            {
                return;
            }

            string[] windowRootArr = setting.uiManagerSetting.WindowPrefabFolderPathArr;

            bool needUpdate = false;
            foreach (var item in windowRootArr)
            {
                string[] filePathArr = Directory.GetFiles(Application.dataPath.Replace("Assets", "") + item, "*.prefab",
                    SearchOption.AllDirectories);
                foreach (var path in filePathArr)
                {
                    if (path.EndsWith(".meta")) continue;
                    WindowConfigData windowData = windowConfig.GetWindowData(Path.GetFileNameWithoutExtension(path), false);

                    string windowPath = windowData == null ? string.Empty : windowData.windowPrefabPath;
                    if (string.IsNullOrEmpty(windowPath) || (!string.IsNullOrEmpty(windowPath) &&
                                                             windowPath.GetHashCode() != path.GetHashCode()))
                    {
                        needUpdate = true;
                        break;
                    }
                }
            }

            if (!needUpdate)
            {
                Debug.Log("Window prefab config is unchanged; skip generation.");
                return;
            }

            windowConfig.windowConfigList.Clear();
            foreach (var item in windowRootArr)
            {
                string folder = Application.dataPath.Replace("Assets", "") + item;
                string[] filePathArr = Directory.GetFiles(folder, "*.prefab", SearchOption.AllDirectories);
                foreach (var path in filePathArr)
                {
                    if (path.EndsWith(".meta"))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    WindowConfigData data = new WindowConfigData { windowName = fileName, windowPrefabPath = fileName };
                    windowConfig.windowConfigList.Add(data);
                }
            }

            EditorUtility.SetDirty(windowConfig);
            AssetDatabase.SaveAssetIfDirty(windowConfig);
        }

        private static WSFrameSetting GetSetting()
        {
            var settings = AssetDatabase.FindAssets("t:WSFrameSetting");
            if (settings.Length == 0)
            {
                Debug.LogError("Can not find WSFrameSetting asset.");
                return null;
            }

            var path = AssetDatabase.GUIDToAssetPath(settings[0]);
            return AssetDatabase.LoadAssetAtPath<WSFrameSetting>(path);
        }
    }
}
