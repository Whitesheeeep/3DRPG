using System;
using System.Collections.Generic;
using System.Text;
using RPG.CurrencySystemNS;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Views.Bag;
using RPG.Game.UI.Views.Common;
using RPG.ItemSystem;
using RPG.RewardSystemNS;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Task
{
    /// <summary>管理 TaskDetailsPanel 的任务目标、奖励预览与底部操作显示。</summary>
    [DisallowMultipleComponent]
    [InfoBox("挂在 TaskDetailsPanel；控件引用来自该详情与底部操作区。奖励列表依赖 BagItem.prefab 和已初始化 PoolManager。")]
    public sealed class TaskDetailsPanelView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private TMP_Text taskTitle;
        [SerializeField, Required] private TMP_Text taskDescription;
        [SerializeField, Required] private RectTransform objectiveRowsContainer;
        [SerializeField, Required] private TaskObjectiveRowView objectiveRowPrefab;
        [SerializeField, Required] private TMP_Text claimFailureText;
        [SerializeField, Required] private HorizontalBagItemListView rewardListView;
        [SerializeField] private Sprite molaIcon;
        [SerializeField] private Sprite yuanShiIcon;
        [SerializeField, MinValue(1), MaxValue(5)] private int currencyDisplayRarity = 1;
        [SerializeField, Required] private GameObject actionButtonContainer;
        [SerializeField, Required] private Button trackButton;
        [SerializeField, Required] private TMP_Text trackButtonLabel;
        [SerializeField, Required] private Button claimButton;
        [SerializeField, Required] private TMP_Text claimButtonLabel;
        #endregion

        #region 展示与交互状态
        private readonly List<TaskObjectiveRowView> objectiveRowViews = new List<TaskObjectiveRowView>();
        private readonly List<TaskObjectiveRowView> createdObjectiveRowViews = new List<TaskObjectiveRowView>();
        private string rewardSignature = null;
        #endregion

        #region 事件
        /// <summary>玩家请求切换当前任务的追踪状态时触发。</summary>
        public event Action TrackRequested;
        /// <summary>玩家请求领取当前任务奖励时触发。</summary>
        public event Action ClaimRequested;
        #endregion

        #region 生命周期与校验
        /// <summary>校验序列化引用并绑定固定按钮的本地用户意图。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            trackButton.onClick.AddListener(HandleTrackClicked);
            claimButton.onClick.AddListener(HandleClaimClicked);
            claimFailureText.gameObject.SetActive(false);
            claimButton.gameObject.SetActive(false);
            actionButtonContainer.SetActive(false);
        }

        /// <summary>避免组件销毁后 Button 保留已失效的 View 实例回调。</summary>
        private void OnDestroy()
        {
            if (trackButton != null) trackButton.onClick.RemoveListener(HandleTrackClicked);
            if (claimButton != null) claimButton.onClick.RemoveListener(HandleClaimClicked);
            for (int index = 0; index < createdObjectiveRowViews.Count; index++)
                if (createdObjectiveRowViews[index] != null) UnityEngine.Object.Destroy(createdObjectiveRowViews[index].gameObject);
            createdObjectiveRowViews.Clear();
            objectiveRowViews.Clear();
            TrackRequested = null;
            ClaimRequested = null;
        }

        /// <summary>检查详情文本、目标模板、奖励列表和固定操作按钮。</summary>
        public void ValidateConfiguration()
        {
            if (taskTitle == null || taskDescription == null || objectiveRowsContainer == null || objectiveRowPrefab == null ||
                claimFailureText == null || rewardListView == null || actionButtonContainer == null ||
                trackButton == null || trackButtonLabel == null || claimButton == null || claimButtonLabel == null)
                throw new InvalidOperationException("[TaskDetailsPanelView] 详情面板存在未绑定的必需引用。");
            objectiveRowPrefab.ValidateConfiguration();
            rewardListView.ValidateConfiguration();
            if (!trackButton.transform.IsChildOf(actionButtonContainer.transform) ||
                !claimButton.transform.IsChildOf(actionButtonContainer.transform))
                throw new InvalidOperationException("[TaskDetailsPanelView] 追踪与领奖按钮必须属于固定操作栏。");
        }

        /// <summary>详情尺寸改变后请求奖励列表重新计算条目比例。</summary>
        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled && rewardListView != null) rewardListView.RefreshLayoutSize();
        }
        #endregion

        #region 详情展示
        /// <summary>刷新任务描述、目标进度、操作按钮和只读奖励预览。</summary>
        /// <param name="definition">任务静态定义；为空时隐藏详情。</param>
        /// <param name="record">任务当前玩家状态；为空时隐藏详情。</param>
        /// <param name="tracked">任务是否正在追踪。</param>
        /// <param name="failureMessage">最近一次可重试的领奖失败原因。</param>
        /// <param name="spriteResolver">按图集地址和 Sprite 名称查询当前已加载图标。</param>
        public void Render(
            TaskDefinition definition,
            TaskRecord record,
            bool tracked,
            string failureMessage,
            Func<string, string, Sprite> spriteResolver)
        {
            bool hasSelection = definition != null && record != null;
            if (!hasSelection)
            {
                actionButtonContainer.SetActive(false);
                gameObject.SetActive(false);
                HideObjectiveRows();
                claimFailureText.text = string.Empty;
                claimFailureText.gameObject.SetActive(false);
                BindRewardsIfChanged(string.Empty, new List<BagItemViewData>());
                return;
            }

            gameObject.SetActive(true);
            actionButtonContainer.SetActive(true);
            taskTitle.text = definition.Title;
            taskDescription.text = definition.Description;
            if (!definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition stage, out _))
                throw new InvalidOperationException($"任务 {record.TaskId} 当前阶段缺少定义。");

            // 详情不单独显示阶段标题；目标行使用目标说明或阶段标题作为说明回退。
            for (int index = 0; index < stage.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = stage.Objectives[index];
                if (!record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                    throw new InvalidOperationException($"任务 {record.TaskId} 当前阶段缺少目标进度：{objective.ObjectiveId}。");
                string description = string.IsNullOrWhiteSpace(objective.DisplayDescription)
                    ? (string.IsNullOrWhiteSpace(stage.Title) ? "完成目标" : stage.Title)
                    : objective.DisplayDescription;
                GetOrCreateObjectiveRow(index).BindObjective(
                    description, progress.Current, progress.Required, progress.IsComplete);
            }
            HideObjectiveRows(stage.Objectives.Count);

            bool hasFailure = !string.IsNullOrWhiteSpace(failureMessage);
            claimFailureText.text = hasFailure ? failureMessage : string.Empty;
            claimFailureText.gameObject.SetActive(hasFailure);
            trackButton.gameObject.SetActive(true);
            trackButtonLabel.text = tracked ? "取消追踪" : "追踪任务";
            claimButton.gameObject.SetActive(record.State == E_TaskLifecycleState.Claimable);
            claimButtonLabel.text = "领取奖励";
            RenderRewards(definition.Rewards, spriteResolver);
            rewardListView.RefreshLayoutSize();
        }

        /// <summary>复用已创建的目标行，必要时从独立目标行 Prefab 创建一行。</summary>
        /// <param name="index">目标在当前阶段中的排序位置。</param>
        /// <returns>可绑定目标进度的行视图。</returns>
        private TaskObjectiveRowView GetOrCreateObjectiveRow(int index)
        {
            if (index < objectiveRowViews.Count) return objectiveRowViews[index];
            TaskObjectiveRowView rowView = UnityEngine.Object.Instantiate(objectiveRowPrefab, objectiveRowsContainer, false);
            GameObject rowObject = rowView.gameObject;
            rowObject.name = $"TaskObjectiveRow_{index + 1}";
            objectiveRowViews.Add(rowView);
            createdObjectiveRowViews.Add(rowView);
            rowObject.transform.SetSiblingIndex(index);
            rowObject.SetActive(false);
            Debug.Log($"[TaskDetailsPanelView] 按需创建目标行，index={index}");
            return rowView;
        }

        /// <summary>隐藏不属于当前阶段的目标行。</summary>
        /// <param name="activeCount">当前阶段目标数；零表示隐藏全部行。</param>
        private void HideObjectiveRows(int activeCount = 0)
        {
            for (int index = activeCount; index < objectiveRowViews.Count; index++) objectiveRowViews[index].Hide();
        }
        #endregion

        #region 奖励预览
        /// <summary>将合并后的货币和 Item 奖励映射为 BagItem 横向只读条目。</summary>
        /// <param name="rewards">任务配置的通用奖励列表。</param>
        /// <param name="spriteResolver">当前窗口持有的图标查询入口。</param>
        private void RenderRewards(IReadOnlyList<RewardDefinition> rewards, Func<string, string, Sprite> spriteResolver)
        {
            List<RewardDisplayEntry> rewardEntries = BuildRewardEntries(rewards);
            var viewData = new List<BagItemViewData>(rewardEntries.Count);
            var signatureBuilder = new StringBuilder();
            for (int index = 0; index < rewardEntries.Count; index++)
            {
                RewardDisplayEntry entry = rewardEntries[index];
                Sprite icon = entry.FixedIcon;
                if (icon == null && spriteResolver != null && entry.IconAddress.Length > 0)
                    icon = spriteResolver(entry.IconAddress, entry.IconSpriteName);

                signatureBuilder.Append(entry.CurrencyId).Append('|').Append(entry.ItemId).Append('|')
                    .Append(entry.Quantity).Append('|').Append(entry.Rarity).Append('|')
                    .Append(icon == null ? 0 : icon.GetInstanceID()).Append('\n');
                viewData.Add(new BagItemViewData(
                    default,
                    entry.DisplayName,
                    entry.CurrencyId != CurrencyId.None ? currencyDisplayRarity : entry.Rarity,
                    $"×{entry.Quantity}",
                    icon,
                    null,
                    string.Empty,
                    false,
                    false,
                    false));
            }

            BindRewardsIfChanged(signatureBuilder.ToString(), viewData);
        }

        /// <summary>只在奖励定义或异步图标结果变化时重绑对象池条目。</summary>
        /// <param name="nextSignature">奖励身份、数量、品质及图标构成的展示签名。</param>
        /// <param name="entries">BagItem 可读展示数据。</param>
        private void BindRewardsIfChanged(string nextSignature, IReadOnlyList<BagItemViewData> entries)
        {
            if (rewardSignature == nextSignature) return;
            rewardSignature = nextSignature;
            rewardListView.Bind(entries);
        }

        /// <summary>合并重复货币和物品 ID，并保留它们首次出现在定义中的次序。</summary>
        /// <param name="rewards">通用奖励定义。</param>
        /// <returns>合并后的图标与数量来源数据。</returns>
        private List<RewardDisplayEntry> BuildRewardEntries(IReadOnlyList<RewardDefinition> rewards)
        {
            var entries = new List<RewardDisplayEntry>();
            var rewardIndexByCurrencyIdMap = new Dictionary<CurrencyId, int>();
            var rewardIndexByItemIdMap = new Dictionary<ItemId, int>();
            for (int rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
            {
                switch (rewards[rewardIndex])
                {
                    case CurrencyRewardDefinition currencyReward:
                        for (int entryIndex = 0; entryIndex < currencyReward.Amounts.Count; entryIndex++)
                        {
                            CurrencyRewardEntry amount = currencyReward.Amounts[entryIndex];
                            if (rewardIndexByCurrencyIdMap.TryGetValue(amount.CurrencyId, out int existingIndex))
                            {
                                entries[existingIndex].Quantity = checked(entries[existingIndex].Quantity + amount.Amount);
                                continue;
                            }

                            string displayName = amount.CurrencyId == CurrencyId.Mola ? "摩拉" :
                                amount.CurrencyId == CurrencyId.YuanShi ? "原石" : amount.CurrencyId.ToString();
                            Sprite icon = amount.CurrencyId == CurrencyId.Mola ? molaIcon :
                                amount.CurrencyId == CurrencyId.YuanShi ? yuanShiIcon : null;
                            rewardIndexByCurrencyIdMap.Add(amount.CurrencyId, entries.Count);
                            entries.Add(new RewardDisplayEntry(amount.CurrencyId, default, displayName,
                                amount.Amount, icon, string.Empty, string.Empty, 0));
                        }
                        break;
                    case ItemRewardDefinition itemReward:
                        for (int entryIndex = 0; entryIndex < itemReward.Items.Count; entryIndex++)
                        {
                            ItemRewardEntry item = itemReward.Items[entryIndex];
                            if (rewardIndexByItemIdMap.TryGetValue(item.ItemId, out int existingIndex))
                            {
                                entries[existingIndex].Quantity = checked(entries[existingIndex].Quantity + item.Quantity);
                                continue;
                            }

                            string displayName = item.ItemId.ToString();
                            string iconAddress = string.Empty;
                            string iconSpriteName = string.Empty;
                            int rarity = 0;
                            if (ItemManager.Instance.IsConfigured &&
                                ItemManager.Instance.TryGetDefinition(item.ItemId, out ItemDefinition itemDefinition))
                            {
                                displayName = itemDefinition.DisplayName;
                                iconAddress = itemDefinition.IconAddress ?? string.Empty;
                                iconSpriteName = itemDefinition.IconSpriteName ?? string.Empty;
                                rarity = (int)itemDefinition.Rarity;
                            }

                            rewardIndexByItemIdMap.Add(item.ItemId, entries.Count);
                            entries.Add(new RewardDisplayEntry(CurrencyId.None, item.ItemId, displayName,
                                item.Quantity, null, iconAddress, iconSpriteName, rarity));
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"[TaskDetailsPanelView] 不支持展示奖励类型 {rewards[rewardIndex]?.GetType().FullName ?? "<null>"}。");
                }
            }
            return entries;
        }

        /// <summary>转发追踪按钮上的用户操作意图。</summary>
        private void HandleTrackClicked() => TrackRequested?.Invoke();

        /// <summary>转发领取奖励按钮上的用户操作意图。</summary>
        private void HandleClaimClicked() => ClaimRequested?.Invoke();
        #endregion

        #region 奖励数据
        /// <summary>保存合并奖励的身份、显示名称、数量及图标来源。</summary>
        private sealed class RewardDisplayEntry
        {
            /// <summary>创建合并后的奖励预览条目。</summary>
            /// <param name="currencyId">货币 ID；Item 奖励使用 None。</param>
            /// <param name="itemId">Item ID；货币奖励使用默认值。</param>
            /// <param name="displayName">本地化名称或配置标题。</param>
            /// <param name="quantity">合并后的数量。</param>
            /// <param name="fixedIcon">Prefab 显式配置的货币图标。</param>
            /// <param name="iconAddress">Item 图集地址。</param>
            /// <param name="iconSpriteName">图集内 Sprite 名称。</param>
            /// <param name="rarity">Item 配置的品质。</param>
            public RewardDisplayEntry(CurrencyId currencyId, ItemId itemId, string displayName, int quantity,
                Sprite fixedIcon, string iconAddress, string iconSpriteName, int rarity)
            {
                CurrencyId = currencyId;
                ItemId = itemId;
                DisplayName = displayName;
                Quantity = quantity;
                FixedIcon = fixedIcon;
                IconAddress = iconAddress;
                IconSpriteName = iconSpriteName;
                Rarity = rarity;
            }

            /// <summary>获取货币 ID。</summary>
            public CurrencyId CurrencyId { get; }
            /// <summary>获取 Item ID。</summary>
            public ItemId ItemId { get; }
            /// <summary>获取显示名称。</summary>
            public string DisplayName { get; }
            /// <summary>获取或设置合并后的数量。</summary>
            public int Quantity { get; set; }
            /// <summary>获取显式货币图标。</summary>
            public Sprite FixedIcon { get; }
            /// <summary>获取 Item 图集地址。</summary>
            public string IconAddress { get; }
            /// <summary>获取图集中 Sprite 的名称。</summary>
            public string IconSpriteName { get; }
            /// <summary>获取 Item 品质数值。</summary>
            public int Rarity { get; }
        }
        #endregion
    }
}
