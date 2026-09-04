#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Baking.Editor
{
    /// <summary>为通用烘焙数据源提供统一 Undo、Dirty 和保存事务。</summary>
    public sealed class BakedResultEditorService
    {
        /// <summary>创建通用烘焙事务服务。</summary>
        public BakedResultEditorService()
        {
        }

        /// <summary>执行一次原子 Bake 事务。</summary>
        /// <param name="source">待烘焙数据源。</param>
        /// <exception cref="ArgumentNullException">数据源为空时抛出。</exception>
        public void Bake(IBakedResultDataSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            IReadOnlyList<UnityEngine.Object> targetList = source.BakeTargets ?? throw new InvalidOperationException($"烘焙数据源 '{source.BakedResultTitle}' 返回了空的 BakeTargets。");
            if (targetList.Any(target => target == null)) throw new InvalidOperationException($"烘焙数据源 '{source.BakedResultTitle}' 的 BakeTargets 包含空对象。");

            // 先切换到独立组，再读取 Unity 实际分配的组号；IncrementCurrentGroup 本身无返回值。
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"烘焙结果：{source.BakedResultTitle}");
            try
            {
                if (targetList.Count > 0) Undo.RecordObjects(targetList.ToArray(), $"烘焙结果：{source.BakedResultTitle}");
                source.Bake();
                BakedResultTableData result = source.CreateBakedResultTableData();
                for (int index = 0; index < targetList.Count; index++)
                    if (EditorUtility.IsPersistent(targetList[index])) EditorUtility.SetDirty(targetList[index]);
                if (targetList.Count > 0) AssetDatabase.SaveAssets();
                _ = result;
                Undo.CollapseUndoOperations(undoGroup);
                // 关闭本次事务边界，避免后续 PropertyField 写入沿用 Bake 组。
                Undo.IncrementCurrentGroup();
                // 所有已打开的结果窗口共享同一个数据源快照，烘焙完成后统一刷新。
                BakedResultViewerWindow.RefreshIfDisplaying(source);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Undo.IncrementCurrentGroup();
                throw;
            }
        }
    }
}
#endif
