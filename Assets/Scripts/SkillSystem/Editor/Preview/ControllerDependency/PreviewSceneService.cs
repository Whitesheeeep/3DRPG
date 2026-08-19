#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理预览场景、演示角色与摄像机设置，并提供打开预览场景的功能。
    /// </summary>
    internal sealed class PreviewSceneService : System.IDisposable
    {
        private readonly EditorSettings settings;

        public event System.Action SettingsChanged;

        public SceneAsset PreviewScene => settings.PreviewScene;
        public GameObject PreviewActor => settings.PreviewActor;
        public GameObject GameplayCameraPrefab => settings.GameplayCameraPrefab;
        public bool PreviewCameraModifier => settings.PreviewCameraModifier;
        public bool IsPreviewSceneLoaded
        {
            get
            {
                SceneAsset scene = settings.PreviewScene;
                if (scene == null) return false;
                string path = AssetDatabase.GetAssetPath(scene);
                if (string.IsNullOrEmpty(path)) return false;
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene loadedScene = SceneManager.GetSceneAt(index);
                    if (loadedScene.IsValid() && loadedScene.isLoaded && loadedScene.path == path)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 创建并初始化 PreviewSceneService。
        /// </summary>
        public PreviewSceneService(EditorSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// 释放事件订阅和该对象持有的编辑器资源。
        /// </summary>
        public void Dispose() => SettingsChanged = null;

        /// <summary>
        /// 保存固定预览场景的资产 GUID。
        /// </summary>
        public void SetPreviewScene(SceneAsset scene)
        {
            settings.SetPreviewScene(scene);
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// 保存固定演示角色的 GlobalObjectId。
        /// </summary>
        public void SetPreviewActor(GameObject actor)
        {
            settings.SetPreviewActor(actor);
            SettingsChanged?.Invoke();
        }

        /// <summary>保存 Gameplay VCam Prefab 并通知 Inspector 刷新参考 FOV。</summary>
        public void SetGameplayCameraPrefab(GameObject prefab)
        {
            settings.SetGameplayCameraPrefab(prefab);
            SettingsChanged?.Invoke();
        }

        /// <summary>切换 Scene View 摄像机修饰预览。</summary>
        public void SetPreviewCameraModifier(bool value)
        {
            settings.SetPreviewCameraModifier(value);
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// 保存当前场景后以 Additive 加载固定预览场景，并将其设为 Active Scene。
        /// </summary>
        public bool OpenPreviewScene()
        {
            SceneAsset scene = settings.PreviewScene;
            if (scene == null)
            {
                EditorUtility.DisplayDialog("技能时间轴", "请先选择编辑器预览场景。", "确定");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            string path = AssetDatabase.GetAssetPath(scene);
            if (string.IsNullOrEmpty(path)) return false;
            Scene previewScene = SceneManager.GetSceneByPath(path);
            if (!previewScene.IsValid() || !previewScene.isLoaded)
                previewScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (!previewScene.IsValid() || !previewScene.isLoaded)
            {
                EditorUtility.DisplayDialog("技能时间轴", "无法加载编辑器预览场景。", "确定");
                return false;
            }

            // 保留原有场景，只切换 Active Scene，确保预览副本进入刚加载的测试场景。
            if (!SceneManager.SetActiveScene(previewScene))
            {
                EditorUtility.DisplayDialog("技能时间轴", "无法将预览场景设置为 Active Scene。预览场景可能已经是 Active Scene。", "确定");
                return false;
            }
            SettingsChanged?.Invoke();
            return true;
        }
    }
}
#endif
