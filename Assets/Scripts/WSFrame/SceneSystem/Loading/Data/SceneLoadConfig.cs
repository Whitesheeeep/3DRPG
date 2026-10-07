using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WS_Modules.SceneModule
{
    /// <summary>保存场景稳定标识、Addressables 场景地址和可组合任务树的场景配置。</summary>
    [CreateAssetMenu(fileName = "SceneLoadConfig", menuName = "WSFrame/Scene Loading/Scene Config")]
    public sealed class SceneLoadConfig : ScriptableObject
    {
        #region 配置字段

        [SerializeField, Tooltip("存档与代码使用的稳定场景 ID。")]
        private string sceneId;
        [SerializeField, Tooltip("由场景 Reference 自动生成；用于加载结果校验和直接打开场景时匹配。")]
        private string sceneName;
        [SerializeField, Tooltip("Editor 中显示的友好名称。")]
        private string displayName;
        [SerializeField, Tooltip("实际加载依据。场景名称从此引用自动生成。")]
        private SceneAssetReference sceneReference;
        [SerializeField, Tooltip("Single 替换当前场景；Additive 在当前场景上追加。")]
        private LoadSceneMode loadMode = LoadSceneMode.Single;
        [SerializeField, Tooltip("包含场景加载节点及加载前后任务的根组合节点。")]
        private SceneLoadTask rootTask;

        #endregion

        #region 属性

        /// <summary>获取稳定场景 ID。</summary>
        public string SceneId => sceneId;
        /// <summary>获取从场景资源引用自动生成的场景名，用于校验和当前场景匹配。</summary>
        public string SceneName => sceneName;
        /// <summary>获取 Editor 与日志显示名称。</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? sceneName : displayName;
        /// <summary>获取 Addressables 场景资源引用。</summary>
        public AssetReference SceneReference => sceneReference;
        /// <summary>获取配置的 Unity 场景加载模式。</summary>
        public LoadSceneMode LoadMode => loadMode;
        /// <summary>获取可嵌套的根任务节点。</summary>
        public SceneLoadTask RootTask => rootTask;

        #endregion

#if UNITY_EDITOR
        #region 编辑器场景名称同步

        /// <summary>在 Inspector 验证或 Project 刷新后，使校验名称与场景资源名称一致。</summary>
        private void OnValidate()
        {
            SynchronizeSceneNameFromReference();
        }

        /// <summary>按当前 Addressables 场景引用更新序列化场景名。</summary>
        /// <returns>名称实际变化时返回 true。</returns>
        public bool SynchronizeSceneNameFromReference()
        {
            string resolvedSceneName = sceneReference != null && sceneReference.editorAsset != null
                ? sceneReference.editorAsset.name
                : string.Empty;
            if (string.Equals(sceneName, resolvedSceneName, System.StringComparison.Ordinal)) return false;

            string previousSceneName = sceneName;
            sceneName = resolvedSceneName;
            EditorUtility.SetDirty(this);
            WSLog.Log($"[SceneLoadConfig] 已从场景 Reference 同步 SceneName，sceneId={sceneId}，previous={previousSceneName}，current={sceneName}。");
            return true;
        }

        #endregion
#endif
    }
}
