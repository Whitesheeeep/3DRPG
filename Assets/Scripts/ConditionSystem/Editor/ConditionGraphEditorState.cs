#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>将上次打开的资产和画布变换保存在 Library，不将窗口状态混入业务配置。</summary>
    [FilePath("Library/ConditionGraphEditorState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ConditionGraphEditorState : ScriptableSingleton<ConditionGraphEditorState>
    {
        #region 编辑器状态
        [SerializeField] private string assetGuid;
        [SerializeField] private Vector3 position;
        [SerializeField] private Vector3 scale = Vector3.one;
        internal Vector3 Position => position;
        internal Vector3 Scale => scale;
        #endregion

        #region 状态保存与恢复
        // 状态恢复：使用 GUID 解析仍然有效的目标。
        /// <summary>解析上次资产，移动资源后仍通过 GUID 定位。</summary>
        /// <returns>仍然存在的条件图或 null。</returns>
        internal ConditionGraph_SO ResolveAsset() =>
            AssetDatabase.LoadAssetAtPath<ConditionGraph_SO>(AssetDatabase.GUIDToAssetPath(assetGuid));

        // 状态保存：仅写入 Library 下的编辑器状态。
        /// <summary>保存当前窗口所属资产及画布变换。</summary>
        /// <param name="graph">当前图。</param>
        /// <param name="viewPosition">画布位置。</param>
        /// <param name="viewScale">画布缩放。</param>
        internal void Store(ConditionGraph_SO graph, Vector3 viewPosition, Vector3 viewScale)
        {
            assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(graph));
            position = viewPosition;
            scale = viewScale;
            Save(true);
        }
        #endregion
    }
}
#endif
