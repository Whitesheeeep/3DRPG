#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Cinemachine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存技能时间轴编辑器的固定预览场景、演示角色和摄像机预览设置。
    /// </summary>
    [FilePath("ProjectSettings/SkillTimelineEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class EditorSettings : ScriptableSingleton<EditorSettings>
    {
        [SerializeField] private string previewSceneGuid = string.Empty;
        [SerializeField] private string selectedSkillConfigGuid = string.Empty;
        [SerializeField] private string previewActorGlobalObjectId = string.Empty;
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

        /// <summary>
        /// 读取上次在技能时间轴编辑器中选择的 SkillConfig 资产。
        /// </summary>
        public SkillConfig SelectedSkillConfig
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(selectedSkillConfigGuid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SkillConfig>(path);
            }
        }

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
        /// 保存技能时间轴当前选择的 SkillConfig 资产 GUID；传入空值时清除历史选择。
        /// </summary>
        /// <param name="config">需要记住的技能配置；为空表示不保留技能选择。</param>
        public void SetSelectedSkillConfig(SkillConfig config)
        {
            // 以资源路径换取 GUID，保证移动资源后仍沿用 Unity 资产的稳定身份。
            string path = config != null ? AssetDatabase.GetAssetPath(config) : string.Empty;
            string nextGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            // 选择未变化时不重复写入 ProjectSettings，避免普通刷新造成无意义的保存。
            if (selectedSkillConfigGuid == nextGuid) return;

            selectedSkillConfigGuid = nextGuid;
            // 与测试场景设置共用 ScriptableSingleton 的项目级保存机制。
            Save(true);
            Debug.Log($"[SkillTimelineEditorSettings] 已保存技能选择：{(config != null ? config.name : "无")}");
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
