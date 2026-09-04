using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WS_Modules.Baking
{
    /// <summary>保存可由通用窗口展示的一次扁平烘焙结果表。</summary>
    public sealed class BakedResultTableData
    {
        #region 字段

        private readonly IReadOnlyList<string> headers;
        private readonly IReadOnlyList<BakedResultRowData> rows;

        #endregion

        #region 属性

        /// <summary>获取表格标题。</summary>
        public string Title { get; }

        /// <summary>获取表头文本。</summary>
        public IReadOnlyList<string> Headers => headers;

        /// <summary>获取最终结果行。</summary>
        public IReadOnlyList<BakedResultRowData> Rows => rows;

        #endregion

        #region 构造

        /// <summary>创建并校验一次扁平烘焙结果表。</summary>
        /// <param name="title">表格标题。</param>
        /// <param name="headers">表头文本。</param>
        /// <param name="rows">结果行。</param>
        /// <exception cref="ArgumentException">表头或行数据不符合表格契约时抛出。</exception>
        public BakedResultTableData(
            string title,
            IReadOnlyList<string> headers,
            IReadOnlyList<BakedResultRowData> rows)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("烘焙结果标题不能为空。", nameof(title));
            if (headers == null || headers.Count == 0) throw new ArgumentException("烘焙结果至少需要一列。", nameof(headers));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            for (int index = 0; index < headers.Count; index++)
                if (string.IsNullOrWhiteSpace(headers[index]))
                    throw new ArgumentException($"烘焙结果第 {index} 列的题头不能为空。", nameof(headers));
            for (int index = 0; index < rows.Count; index++)
                if (rows[index] == null || rows[index].Cells.Count != headers.Count)
                    throw new ArgumentException($"烘焙结果第 {index} 行的列数与题头不一致。", nameof(rows));

            Title = title;
            this.headers = new ReadOnlyCollection<string>(new List<string>(headers));
            this.rows = new ReadOnlyCollection<BakedResultRowData>(new List<BakedResultRowData>(rows));
        }

        #endregion
    }

    /// <summary>保存烘焙结果表中的一行最终显示文本。</summary>
    public sealed class BakedResultRowData
    {
        #region 字段

        private readonly IReadOnlyList<string> cells;

        #endregion

        #region 属性

        /// <summary>获取当前行单元格文本。</summary>
        public IReadOnlyList<string> Cells => cells;

        #endregion

        #region 构造

        /// <summary>创建一行烘焙结果。</summary>
        /// <param name="cells">单元格文本。</param>
        /// <exception cref="ArgumentNullException">单元格列表为空引用时抛出。</exception>
        public BakedResultRowData(IReadOnlyList<string> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            for (int index = 0; index < cells.Count; index++)
                if (cells[index] == null) throw new ArgumentException($"烘焙结果单元格 {index} 不能为空。", nameof(cells));
            this.cells = new ReadOnlyCollection<string>(new List<string>(cells));
        }

        #endregion
    }
}
