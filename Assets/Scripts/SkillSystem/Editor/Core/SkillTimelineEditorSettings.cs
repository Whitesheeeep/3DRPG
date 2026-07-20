#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    [FilePath("ProjectSettings/SkillTimelineEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class SkillTimelineEditorSettings : ScriptableSingleton<SkillTimelineEditorSettings>
    {
        [SerializeField] private string previewSceneGuid = string.Empty;
        [SerializeField] private string previewActorGlobalObjectId = string.Empty;

        public SceneAsset PreviewScene
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(previewSceneGuid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            }
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
    }
}
#endif
