#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.Character.DirectionalLocomotion.Editor
{
    /// <summary>自动绑定 Demo 动画并计算 Walk 根运动平均速度。</summary>
    public static class DirectionalLocomotionSettingBuilder
    {
        public const string RootPath = "Assets/Scripts/Character/DirectionalLocomotion";
        public const string GeneratedPath = RootPath + "/Generated";
        public const string SettingPath = GeneratedPath + "/DirectionalLocomotionSetting.asset";
        private const string AnimationPath = "Assets/Res/Animation/Animation/Animations/FemaleMovementAnimsetPro/Animations/";

        [MenuItem("Tools/RPG/Directional Locomotion/Update Animancer Demo Setting")]
        public static void BuildFromMenu()
        {
            EnsureFolder(GeneratedPath);
            DirectionalLocomotionSetting setting = Selection.activeObject as DirectionalLocomotionSetting;
            if (setting == null) setting = LoadOrCreateSetting();
            Populate(setting);
            Selection.activeObject = setting;
            Debug.Log($"[DirectionalLocomotion] Animancer Demo 配置已更新，Walk 平均速度={setting.walkAverageSpeed:F3} m/s。", setting);
        }

        private static DirectionalLocomotionSetting LoadOrCreateSetting()
        {
            DirectionalLocomotionSetting setting = AssetDatabase.LoadAssetAtPath<DirectionalLocomotionSetting>(SettingPath);
            if (setting != null) return setting;
            setting = ScriptableObject.CreateInstance<DirectionalLocomotionSetting>();
            AssetDatabase.CreateAsset(setting, SettingPath);
            return setting;
        }

        private static void Populate(DirectionalLocomotionSetting setting)
        {
            setting.idle = LoadClip("Idle");
            setting.walkForward = LoadClip("WalkFwd");
            setting.startLeft135 = LoadClip("WalkFwdStart_L135");
            setting.startLeft90 = LoadClip("WalkFwdStart_L90");
            setting.startLeft45 = LoadClip("WalkFwdStart_L45");
            setting.startForward = LoadClip("WalkFwdStart");
            setting.startRight45 = LoadClip("WalkFwdStart_R45");
            setting.startRight90 = LoadClip("WalkFwdStart_R90");
            setting.startRight135 = LoadClip("WalkFwdStart_R135");
            setting.startRight180 = LoadClip("WalkFwdStart_R180");

            Vector3 averageSpeed = setting.walkForward.averageSpeed;
            setting.walkAverageSpeed = new Vector2(averageSpeed.x, averageSpeed.z).magnitude;
            if (setting.walkAverageSpeed <= 0.001f)
                throw new InvalidOperationException("WalkFwd.averageSpeed 接近零，请检查动画 Root Transform 导入设置后再更新配置。");
            setting.walkSpeed = setting.walkAverageSpeed;
            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip LoadClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationPath + name + ".anim");
            if (clip == null) throw new InvalidOperationException($"找不到动画：{AnimationPath}{name}.anim");
            return clip;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
