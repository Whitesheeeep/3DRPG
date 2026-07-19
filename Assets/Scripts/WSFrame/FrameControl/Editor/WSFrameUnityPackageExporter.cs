using System.IO;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.EditorTools
{
    internal static class WSFrameUnityPackageExporter
    {
        private const string ExportFolder = "Builds/UnityPackages";
        private const string MainPackagePath = ExportFolder + "/WSFrame.unitypackage";
        private const string SamplesPackagePath = ExportFolder + "/WSFrame.Samples.unitypackage";
        private const string ManualTestsPackagePath = ExportFolder + "/WSFrame.ManualTests.unitypackage";

        private static readonly string[] MainPackageAssets =
        {
            "Assets/Scripts/WSFrame/AudioSystem",
            "Assets/Scripts/WSFrame/ConfigInstaller",
            "Assets/Scripts/WSFrame/Core",
            "Assets/Scripts/WSFrame/FrameControl",
            "Assets/Scripts/WSFrame/FSM",
            "Assets/Scripts/WSFrame/Pooling",
            "Assets/Scripts/WSFrame/ResSystem",
            "Assets/Scripts/WSFrame/SceneSystem",
            "Assets/Scripts/WSFrame/UISystem/Config",
            "Assets/Scripts/WSFrame/UISystem/Core",
            "Assets/Scripts/WSFrame/UISystem/Editor",
            "Assets/Scripts/WSFrame/UISystem/Resources",
            "Assets/Scripts/WSFrame/UISystem/Template",
            "Assets/Scripts/WSFrame/UISystem/WSFrame.UI.asmdef",
            "Assets/Scripts/WSFrame/UIToolkitExtensions",
            "Assets/Scripts/WSFrame/Utilities",
        };

        private static readonly string[] SamplesPackageAssets =
        {
            "Assets/Scripts/WSFrame/UISystem/Samples",
        };

        private static readonly string[] ManualTestsPackageAssets =
        {
            "Assets/Scripts/WSFrame/Tests",
        };

        [MenuItem("Tools/WSFrame/Export UnityPackage/Main")]
        private static void ExportMainPackage()
        {
            ExportPackage(MainPackageAssets, MainPackagePath);
        }

        [MenuItem("Tools/WSFrame/Export UnityPackage/Samples")]
        private static void ExportSamplesPackage()
        {
            ExportPackage(SamplesPackageAssets, SamplesPackagePath);
        }

        [MenuItem("Tools/WSFrame/Export UnityPackage/Manual Tests")]
        private static void ExportManualTestsPackage()
        {
            ExportPackage(ManualTestsPackageAssets, ManualTestsPackagePath);
        }

        [MenuItem("Tools/WSFrame/Export UnityPackage/All")]
        private static void ExportAllPackages()
        {
            ExportMainPackage();
            ExportSamplesPackage();
            ExportManualTestsPackage();
        }

        private static void ExportPackage(string[] assetPaths, string packagePath)
        {
            Directory.CreateDirectory(ExportFolder);
            AssetDatabase.ExportPackage(
                assetPaths,
                packagePath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            Debug.Log($"[WSFrame] Exported unitypackage: {packagePath}");
        }
    }
}
