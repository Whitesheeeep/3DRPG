#if UNITY_EDITOR
using System;
using Cinemachine;
using RPG.SkillSystem;
using UnityEditor;
using UnityEngine;

namespace RPG.CameraSystem.Editor
{
    /// <summary>
    /// 幂等创建项目默认 Camera Shake 与 Impulse 学习预设，不覆盖已存在资产。
    /// </summary>
    internal static class CameraShakePresetGenerator
    {
        #region 常量

        private const string RootFolder = "Assets/Settings/Camera/Shakes";
        private const string CinemachineNoiseFolder = "Packages/com.unity.cinemachine/Presets/Noise";

        #endregion

        #region 菜单入口

        /// <summary>
        /// 创建缺失的 NoiseSettings 与六个默认 Shake Profile，并保留用户已调整的资产。
        /// </summary>
        [MenuItem("Tools/RPG/Camera/Create Default Shake Presets")]
        public static void CreateDefaultPresets()
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Settings/Camera");
            EnsureFolder(RootFolder);

            NoiseSettings runningNoise = CopyNoiseIfMissing(
                "Noise_Running", "Handheld_normal_mild.asset");
            NoiseSettings chargingNoise = CopyNoiseIfMissing(
                "Noise_Charging", "6D Wobble.asset");

            CreateShakeIfMissing("Shake_Running", runningNoise,
                0.35f, 1.15f, CameraShakeLifetime.Sustained,
                0f, 0.15f, 0.25f, CameraModifierBlendMode.Additive);
            CreateShakeIfMissing("Shake_Charging", chargingNoise,
                0.6f, 1.8f, CameraShakeLifetime.Sustained,
                0f, 0.25f, 0.2f, CameraModifierBlendMode.Additive);

            CreateImpulseIfMissing("Shake_LightHit",
                CinemachineImpulseDefinition.ImpulseShapes.Bump,
                0.12f, 0.35f, new Vector3(0.15f, -0.2f, -1f));
            CreateImpulseIfMissing("Shake_HeavyHit",
                CinemachineImpulseDefinition.ImpulseShapes.Explosion,
                0.28f, 0.8f, new Vector3(0.25f, -0.35f, -1f));
            CreateImpulseIfMissing("Shake_Landing",
                CinemachineImpulseDefinition.ImpulseShapes.Bump,
                0.2f, 0.6f, Vector3.down);
            CreateImpulseIfMissing("Shake_Explosion",
                CinemachineImpulseDefinition.ImpulseShapes.Explosion,
                0.45f, 1.2f, new Vector3(0.15f, -0.25f, -1f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Camera Shake 默认预设已检查完成：{RootFolder}");
        }

        #endregion

        #region 资产创建

        /// <summary>
        /// 复制 Cinemachine 内置波形到项目目录，已存在时直接复用项目资产。
        /// </summary>
        /// <param name="assetName">项目内 NoiseSettings 名称。</param>
        /// <param name="sourceName">Cinemachine Package 内置资产文件名。</param>
        /// <returns>可由运行时 Profile 引用的项目资产。</returns>
        private static NoiseSettings CopyNoiseIfMissing(string assetName, string sourceName)
        {
            string destination = $"{RootFolder}/{assetName}.asset";
            NoiseSettings existing = AssetDatabase.LoadAssetAtPath<NoiseSettings>(destination);
            if (existing != null) return existing;

            string source = $"{CinemachineNoiseFolder}/{sourceName}";
            if (!AssetDatabase.CopyAsset(source, destination))
                throw new InvalidOperationException($"无法复制 Cinemachine Noise 预设：{source}");
            return AssetDatabase.LoadAssetAtPath<NoiseSettings>(destination);
        }

        /// <summary>
        /// 创建缺失的持续 Shake Profile，并通过 SerializedObject 写入其私有配置。
        /// </summary>
        /// <param name="assetName">资产名。</param>
        /// <param name="noise">项目内 NoiseSettings。</param>
        /// <param name="amplitude">默认幅度倍率。</param>
        /// <param name="frequency">默认频率倍率。</param>
        /// <param name="lifetime">自动结束或持续到 Stop。</param>
        /// <param name="duration">定时生命周期秒数。</param>
        /// <param name="fadeIn">淡入秒数。</param>
        /// <param name="fadeOut">淡出秒数。</param>
        /// <param name="blendMode">Shake 通道混合方式。</param>
        private static void CreateShakeIfMissing(string assetName, NoiseSettings noise,
            float amplitude, float frequency, CameraShakeLifetime lifetime,
            float duration, float fadeIn, float fadeOut, CameraModifierBlendMode blendMode)
        {
            string path = $"{RootFolder}/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<CameraShakeProfile>(path) != null) return;

            CameraShakeProfile profile = ScriptableObject.CreateInstance<CameraShakeProfile>();
            SerializedObject serialized = new(profile);
            serialized.FindProperty("noiseSettings").objectReferenceValue = noise;
            serialized.FindProperty("amplitudeGain").floatValue = amplitude;
            serialized.FindProperty("frequencyGain").floatValue = frequency;
            serialized.FindProperty("lifetime").enumValueIndex = (int)lifetime;
            serialized.FindProperty("duration").floatValue = duration;
            serialized.FindProperty("fadeInDuration").floatValue = fadeIn;
            serialized.FindProperty("fadeOutDuration").floatValue = fadeOut;
            serialized.FindProperty("blendMode").enumValueIndex = (int)blendMode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(profile, path);
        }

        /// <summary>
        /// 创建缺失的 Uniform Impulse Profile，并写入适合学习的基础手感参数。
        /// </summary>
        /// <param name="assetName">资产名。</param>
        /// <param name="shape">Cinemachine 标准冲击波形。</param>
        /// <param name="duration">冲击持续秒数。</param>
        /// <param name="amplitude">Profile 默认强度。</param>
        /// <param name="direction">Profile 默认方向。</param>
        private static void CreateImpulseIfMissing(string assetName,
            CinemachineImpulseDefinition.ImpulseShapes shape,
            float duration, float amplitude, Vector3 direction)
        {
            string path = $"{RootFolder}/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<CameraImpulseProfile>(path) != null) return;

            CameraImpulseProfile profile = ScriptableObject.CreateInstance<CameraImpulseProfile>();
            SerializedObject serialized = new(profile);
            serialized.FindProperty("channel").intValue = 1;
            serialized.FindProperty("shape").enumValueIndex = (int)shape;
            serialized.FindProperty("duration").floatValue = duration;
            serialized.FindProperty("defaultAmplitude").floatValue = amplitude;
            serialized.FindProperty("defaultDirection").vector3Value = direction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(profile, path);
        }

        /// <summary>
        /// 确保目标项目文件夹存在；只创建计划声明的固定路径。
        /// </summary>
        /// <param name="folderPath">以 Assets 开头的项目相对路径。</param>
        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            int separator = folderPath.LastIndexOf('/');
            string parent = folderPath.Substring(0, separator);
            string name = folderPath.Substring(separator + 1);
            AssetDatabase.CreateFolder(parent, name);
        }

        #endregion
    }
}
#endif
