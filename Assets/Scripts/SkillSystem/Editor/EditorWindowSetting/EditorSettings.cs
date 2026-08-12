#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Cinemachine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存技能时间轴编辑器的固定预览场景、演示角色和 Root Motion 设置。
    /// </summary>
    [FilePath("ProjectSettings/SkillTimelineEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class EditorSettings : ScriptableSingleton<EditorSettings>
    {
        [SerializeField] private string previewSceneGuid = string.Empty;
        [SerializeField] private string previewActorGlobalObjectId = string.Empty;
        [SerializeField] private bool previewApplyRootMotion;
        [SerializeField] private string gameplayCameraPrefabGuid = string.Empty;
        [SerializeField] private bool previewCameraModifier;

        public SceneAsset PreviewScene
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(previewSceneGuid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            }
        }

        public bool PreviewApplyRootMotion => previewApplyRootMotion;
        public bool PreviewCameraModifier => previewCameraModifier;
        public GameObject GameplayCameraPrefab
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(gameplayCameraPrefabGuid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }

        /// <summary>读取 Gameplay VCam Prefab 的唯一标准虚拟摄像机参考 FOV。</summary>
        public bool TryGetGameplayReferenceFov(out float fieldOfView)
        {
            fieldOfView = 0f;
            GameObject prefab = GameplayCameraPrefab;
            if (prefab == null) return false;
            CinemachineVirtualCamera[] cameras = prefab.GetComponentsInChildren<CinemachineVirtualCamera>(true);
            if (cameras.Length != 1) return false;
            fieldOfView = cameras[0].m_Lens.FieldOfView;
            return fieldOfView > 0f;
        }

        public GameObject PreviewActor
        {
            get
            {
                if (string.IsNullOrEmpty(previewActorGlobalObjectId) ||
                    !GlobalObjectId.TryParse(previewActorGlobalObjectId, out GlobalObjectId id)) return null;
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            }
        }

        /// <summary>
        /// 保存固定预览场景的资产 GUID。
        /// </summary>
        public void SetPreviewScene(SceneAsset scene)
        {
            string path = scene != null ? AssetDatabase.GetAssetPath(scene) : string.Empty;
            previewSceneGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            Save(true);
        }

        /// <summary>
        /// 保存固定演示角色的 GlobalObjectId。
        /// </summary>
        public void SetPreviewActor(GameObject actor)
        {
            previewActorGlobalObjectId = actor != null
                ? GlobalObjectId.GetGlobalObjectIdSlow(actor).ToString()
                : string.Empty;
            Save(true);
        }

        /// <summary>
        /// 保存动画预览是否应用绝对帧 Root Motion。
        /// </summary>
        public void SetPreviewApplyRootMotion(bool value)
        {
            if (previewApplyRootMotion == value) return;
            previewApplyRootMotion = value;
            Save(true);
        }

        /// <summary>保存用于 FOV 换算的 Gameplay VCam Project Prefab。</summary>
        public void SetGameplayCameraPrefab(GameObject prefab)
        {
            string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : string.Empty;
            gameplayCameraPrefabGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            Save(true);
        }

        /// <summary>保存 Scene View 是否启用摄像机修饰预览。</summary>
        public void SetPreviewCameraModifier(bool value)
        {
            if (previewCameraModifier == value) return;
            previewCameraModifier = value;
            Save(true);
        }
    }
}
#endif
