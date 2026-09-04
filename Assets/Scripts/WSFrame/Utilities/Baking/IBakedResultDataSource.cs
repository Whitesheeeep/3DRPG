using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.Baking
{
    /// <summary>
    /// 为编辑器烘焙结果窗口提供烘焙操作、Undo 目标和最终表格快照。
    /// </summary>
    public interface IBakedResultDataSource
    {
        /// <summary>获取结果窗口标题。</summary>
        string BakedResultTitle { get; }

        /// <summary>获取 Bake 操作会修改的 Unity 资产。</summary>
        IReadOnlyList<Object> BakeTargets { get; }

        /// <summary>执行数据源自己的烘焙逻辑。</summary>
        void Bake();

        /// <summary>创建最近一次保存的最终烘焙结果快照。</summary>
        /// <returns>扁平结果表快照。</returns>
        BakedResultTableData CreateBakedResultTableData();
    }
}
