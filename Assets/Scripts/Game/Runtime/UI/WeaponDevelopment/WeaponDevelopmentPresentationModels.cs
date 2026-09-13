using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;

namespace RPG.Game.UI.WeaponDevelopment
{
    /// <summary>成长页按等级预览所需的只读数据。</summary>
    public sealed class WeaponGrowthPreviewData
    {
        /// <summary>创建成长预览数据。</summary>
        /// <param name="mode">成长展示模式。</param>
        /// <param name="tabLabel">左侧成长入口文案。</param>
        /// <param name="selectMaterialInteractable">选择素材按钮是否可交互。</param>
        /// <param name="autoAddInteractable">自动添加按钮是否可交互。</param>
        /// <param name="selectMaterialLabel">选择素材按钮文案。</param>
        /// <param name="autoAddLabel">自动添加按钮文案。</param>
        /// <param name="selectedExperience">当前已选经验。</param>
        /// <param name="requiredExperience">到达当前等级上限还需经验。</param>
        public WeaponGrowthPreviewData(WeaponGrowthMode mode, string tabLabel,
            bool selectMaterialInteractable, bool autoAddInteractable,
            string selectMaterialLabel, string autoAddLabel,
            long selectedExperience, long requiredExperience)
        {
            Mode = mode;
            TabLabel = tabLabel ?? string.Empty;
            SelectMaterialInteractable = selectMaterialInteractable;
            AutoAddInteractable = autoAddInteractable;
            SelectMaterialLabel = selectMaterialLabel ?? string.Empty;
            AutoAddLabel = autoAddLabel ?? string.Empty;
            SelectedExperience = selectedExperience;
            RequiredExperience = requiredExperience;
        }

        /// <summary>成长展示模式。</summary>
        public WeaponGrowthMode Mode { get; }
        /// <summary>左侧成长入口文案。</summary>
        public string TabLabel { get; }
        /// <summary>选择素材按钮是否可交互。</summary>
        public bool SelectMaterialInteractable { get; }
        /// <summary>自动添加按钮是否可交互。</summary>
        public bool AutoAddInteractable { get; }
        /// <summary>选择素材按钮文案。</summary>
        public string SelectMaterialLabel { get; }
        /// <summary>自动添加按钮文案。</summary>
        public string AutoAddLabel { get; }
        /// <summary>已选素材提供的总经验。</summary>
        public long SelectedExperience { get; }
        /// <summary>到达当前等级上限还需经验。</summary>
        public long RequiredExperience { get; }
    }

    /// <summary>武器培养窗口突破与精炼页面共用的只读展示数据。</summary>
    public sealed class WeaponDevelopmentViewData
    {
        /// <summary>创建页面展示数据。</summary>
        /// <param name="page">页面类型。</param>
        /// <param name="title">页面标题。</param>
        /// <param name="subtitle">页面副标题。</param>
        /// <param name="summaryText">页面顶部比较或阶段摘要文本。</param>
        /// <param name="lines">页面正文行。</param>
        /// <param name="statusText">页面底部状态文本。</param>
        /// <param name="actionInteractable">底部动作按钮是否可交互。</param>
        /// <param name="actionLabel">底部动作按钮文本。</param>
        public WeaponDevelopmentViewData(WeaponDevelopmentPage page, string title, string subtitle,
            string summaryText, IReadOnlyList<string> lines, string statusText,
            bool actionInteractable, string actionLabel, WeaponGrowthPreviewData growthPreview = null)
        {
            Page = page;
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            SummaryText = summaryText ?? string.Empty;
            Lines = lines ?? Array.Empty<string>();
            StatusText = statusText ?? string.Empty;
            ActionInteractable = actionInteractable;
            ActionLabel = actionLabel ?? string.Empty;
            GrowthPreview = growthPreview;
        }

        /// <summary>当前页面。</summary>
        public WeaponDevelopmentPage Page { get; }
        /// <summary>页面标题。</summary>
        public string Title { get; }
        /// <summary>页面副标题。</summary>
        public string Subtitle { get; }
        /// <summary>页面顶部比较或阶段摘要文本。</summary>
        public string SummaryText { get; }
        /// <summary>页面正文行。</summary>
        public IReadOnlyList<string> Lines { get; }
        /// <summary>页面底部状态文本。</summary>
        public string StatusText { get; }
        /// <summary>底部动作按钮是否可交互。</summary>
        public bool ActionInteractable { get; }
        /// <summary>底部动作按钮文本。</summary>
        public string ActionLabel { get; }
        /// <summary>成长页专用预览数据；精炼页为空。</summary>
        public WeaponGrowthPreviewData GrowthPreview { get; }
    }

    /// <summary>精炼材料选择面板的一次绑定数据。</summary>
    public sealed class WeaponRefinementSelectionData
    {
        /// <summary>创建精炼材料选择数据。</summary>
        public WeaponRefinementSelectionData(IReadOnlyList<BagItemViewData> entries, int selectedCount, int maxCount)
        {
            Entries = entries ?? Array.Empty<BagItemViewData>();
            SelectedCount = selectedCount;
            MaxCount = maxCount;
        }

        /// <summary>候选武器列表。</summary>
        public IReadOnlyList<BagItemViewData> Entries { get; }
        /// <summary>当前已选择数量。</summary>
        public int SelectedCount { get; }
        /// <summary>最大选择数量。</summary>
        public int MaxCount { get; }
    }
}
