using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WS_Modules.SceneModule
{
    /// <summary>将 Addressables 引用限制为场景资源，并在运行时保留稳定的引用数据。</summary>
    [Serializable]
    public sealed class SceneAssetReference : AssetReference
    {
        #region 构造

        /// <summary>使用场景资源 GUID 创建 Addressables 场景引用。</summary>
        /// <param name="guid">场景资源的 Addressables GUID。</param>
        public SceneAssetReference(string guid) : base(guid)
        {
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器资源校验

        /// <summary>限定 Project 窗口拖入的资源必须是 Unity 场景资源。</summary>
        /// <param name="asset">待验证的 Unity 资源。</param>
        /// <returns>资源为场景时返回 true。</returns>
        public override bool ValidateAsset(UnityEngine.Object asset) => asset is SceneAsset;

        /// <summary>限定 Addressables 选择器中的路径必须指向 Unity 场景资源。</summary>
        /// <param name="path">待验证资源的项目相对路径。</param>
        /// <returns>路径主资源为场景时返回 true。</returns>
        public override bool ValidateAsset(string path) =>
            !string.IsNullOrWhiteSpace(path) && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(SceneAsset);

        /// <summary>告知 Addressables 编辑器应按 SceneAsset 解析并显示引用资源。</summary>
        protected override Type DerivedClassType => typeof(SceneAsset);

        #endregion
#endif
    }
}
