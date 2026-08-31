#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// 保存 Dialogue Graph Editor 的每项目、每用户轻量编辑状态，不修改对话业务资产。
    /// </summary>
    [FilePath("Library/DialogueGraphEditorState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DialogueGraphEditorState : ScriptableSingleton<DialogueGraphEditorState>
    {
        #region 序列化状态

        // 只保存资产 GUID，避免 Library 状态文件持有 Unity 对象引用或脆弱的资产路径。
        [SerializeField] private string lastAssetGuid = string.Empty;
        [SerializeField] private List<ViewportRecord> viewports = new List<ViewportRecord>();

        #endregion

        #region 运行时状态

        // 平移和缩放事件只改内存；切换资产或窗口释放时再一次性写入 Library 文件。
        [NonSerialized] private bool dirty;

        #endregion

        #region 资产状态

        /// <summary>
        /// 解析上一次编辑的 DialogueAsset；资产已经删除时清理失效 GUID。
        /// </summary>
        /// <returns>仍存在的上次 DialogueAsset；不存在时返回 null。</returns>
        internal DialogueAsset ResolveLastAsset()
        {
            if (string.IsNullOrEmpty(lastAssetGuid)) return null;

            string assetPath = AssetDatabase.GUIDToAssetPath(lastAssetGuid);
            DialogueAsset asset = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<DialogueAsset>(assetPath);
            if (asset != null) return asset;

            // 删除资产后不保留无法解析的 GUID，避免每次打开窗口都尝试加载 Missing 对象。
            lastAssetGuid = string.Empty;
            dirty = true;
            SaveIfDirty();
            return null;
        }

        /// <summary>
        /// 记录当前编辑的 DialogueAsset GUID。
        /// </summary>
        /// <param name="asset">当前资产；传入 null 表示清空最近资产。</param>
        internal void SetLastAsset(DialogueAsset asset)
        {
            string guid = GetAssetGuid(asset);
            if (string.Equals(lastAssetGuid, guid, StringComparison.Ordinal)) return;
            lastAssetGuid = guid;
            dirty = true;
        }

        #endregion

        #region 视口状态

        /// <summary>
        /// 查询指定 DialogueAsset 上次保存的 GraphView 视口。
        /// </summary>
        /// <param name="asset">需要查询的对话资产。</param>
        /// <param name="position">GraphView 视口平移位置。</param>
        /// <param name="scale">GraphView 视口缩放比例。</param>
        /// <returns>存在有效记录时返回 true。</returns>
        internal bool TryGetViewport(DialogueAsset asset, out Vector3 position, out Vector3 scale)
        {
            position = Vector3.zero;
            scale = Vector3.one;
            string guid = GetAssetGuid(asset);
            if (string.IsNullOrEmpty(guid)) return false;

            ViewportRecord record = FindViewport(guid);
            if (record == null) return false;
            position = record.Position;
            scale = record.Scale;
            return true;
        }

        /// <summary>
        /// 在内存中记录指定 DialogueAsset 的 GraphView 视口，不立即写磁盘。
        /// </summary>
        /// <param name="asset">视口所属的对话资产。</param>
        /// <param name="position">GraphView 视口平移位置。</param>
        /// <param name="scale">GraphView 视口缩放比例。</param>
        internal void RecordViewport(DialogueAsset asset, Vector3 position, Vector3 scale)
        {
            string guid = GetAssetGuid(asset);
            if (string.IsNullOrEmpty(guid)) return;
            if (viewports == null) viewports = new List<ViewportRecord>();

            ViewportRecord record = FindViewport(guid);
            if (record == null)
            {
                viewports.Add(new ViewportRecord(guid, position, scale));
                dirty = true;
                return;
            }

            if (record.Position == position && record.Scale == scale) return;
            record.SetTransform(position, scale);
            dirty = true;
        }

        /// <summary>
        /// 将内存中的编辑器状态一次性写入 Library 状态资产。
        /// </summary>
        internal void SaveIfDirty()
        {
            if (!dirty) return;
            Save(true);
            dirty = false;
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 获取资产的稳定 GUID；非持久化对象不产生状态记录。
        /// </summary>
        /// <param name="asset">待解析 GUID 的资产。</param>
        /// <returns>资产 GUID；无法解析时返回空字符串。</returns>
        private static string GetAssetGuid(DialogueAsset asset)
        {
            if (asset == null) return string.Empty;
            string assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
        }

        /// <summary>
        /// 按资产 GUID 查找视口记录。
        /// </summary>
        /// <param name="assetGuid">对话资产 GUID。</param>
        /// <returns>已有视口记录；没有记录时返回 null。</returns>
        private ViewportRecord FindViewport(string assetGuid)
        {
            if (viewports == null) return null;
            for (int index = 0; index < viewports.Count; index++)
            {
                ViewportRecord record = viewports[index];
                if (record != null && string.Equals(record.AssetGuid, assetGuid, StringComparison.Ordinal))
                    return record;
            }

            return null;
        }

        #endregion

        #region 嵌套数据

        /// <summary>
        /// 表示单个 DialogueAsset 的 GraphView 视口快照。
        /// </summary>
        [Serializable]
        private sealed class ViewportRecord
        {
            [SerializeField] private string assetGuid;
            [SerializeField] private Vector3 position;
            [SerializeField] private Vector3 scale;

            /// <summary>创建指定资产的视口记录。</summary>
            /// <param name="assetGuid">资产 GUID。</param>
            /// <param name="position">视口平移位置。</param>
            /// <param name="scale">视口缩放比例。</param>
            internal ViewportRecord(string assetGuid, Vector3 position, Vector3 scale)
            {
                this.assetGuid = assetGuid;
                this.position = position;
                this.scale = scale;
            }

            /// <summary>获取关联资产 GUID。</summary>
            internal string AssetGuid => assetGuid;

            /// <summary>获取视口平移位置。</summary>
            internal Vector3 Position => position;

            /// <summary>获取视口缩放比例。</summary>
            internal Vector3 Scale => scale;

            /// <summary>更新视口变换。</summary>
            /// <param name="newPosition">新的平移位置。</param>
            /// <param name="newScale">新的缩放比例。</param>
            internal void SetTransform(Vector3 newPosition, Vector3 newScale)
            {
                position = newPosition;
                scale = newScale;
            }
        }

        #endregion
    }
}
#endif
