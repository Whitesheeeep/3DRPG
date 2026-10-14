using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using RPG.ItemSystem;
using RPG.RewardSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>标识 DialogueNPC 演示资源包的发放范围。</summary>
    public enum E_DialogueDemoResourceType
    {
        /// <summary>发放摩拉、武器、圣遗物、养成材料和食物。</summary>
        AllResources,
        /// <summary>只发放一万摩拉。</summary>
        Mola10000,
        /// <summary>数据库中每种武器发放一件。</summary>
        Weapons,
        /// <summary>数据库中每种圣遗物发放一件。</summary>
        Artifacts,
        /// <summary>数据库中每种养成材料各发放十个。</summary>
        UpgradeMaterials
    }

    /// <summary>按执行时的 ItemDatabase 内容生成并原子发放一组演示奖励。</summary>
    [Serializable]
    public sealed class DialogueDemoResourceExecution : DialogueAction
    {
        #region 序列化配置

        [SerializeField, LabelText("资源包")]
        private E_DialogueDemoResourceType resourceType;

        #endregion

        #region 构造

        /// <summary>创建供 SerializeReference 和对话命令选择器使用的资源包命令。</summary>
        public DialogueDemoResourceExecution()
        {
        }

        #endregion

        #region 配置校验

        /// <summary>确认序列化的资源包类型属于已支持的演示配置。</summary>
        /// <exception cref="ArgumentOutOfRangeException">资源包类型超出定义范围时抛出。</exception>
        public override void Validate()
        {
            if (!Enum.IsDefined(typeof(E_DialogueDemoResourceType), resourceType))
                throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "未知的 Dialogue Demo 资源包类型。");
        }

        #endregion

        #region 奖励执行

        /// <summary>按当前物品数据库构造奖励，并交给 RewardSystem 做一次完整预检与提交。</summary>
        /// <param name="context">当前对话会话及 Choice 执行上下文。</param>
        /// <exception cref="InvalidOperationException">资源包为空或奖励系统拒绝发放时抛出。</exception>
        public override void Execute(DialogueCommandContext context)
        {
            IReadOnlyList<ItemDefinition> itemDefinitions = ItemManager.Instance.Database.Definitions;
            var itemEntries = new List<ItemRewardEntry>();
            var rewardDefinitions = new List<RewardDefinition>(2);
            int molaAmount = resourceType == E_DialogueDemoResourceType.AllResources ||
                             resourceType == E_DialogueDemoResourceType.Mola10000
                ? 10000
                : 0;

            // 按数据库原有顺序生成物品奖励，使新增的相应分类定义自动纳入演示包。
            for (int index = 0; index < itemDefinitions.Count; index++)
            {
                ItemDefinition definition = itemDefinitions[index];
                if (!TryGetQuantity(definition.Category, out int quantity)) continue;
                itemEntries.Add(new ItemRewardEntry(definition.ItemId, quantity));
            }

            if (molaAmount > 0)
                rewardDefinitions.Add(new CurrencyRewardDefinition(new[]
                {
                    new CurrencyRewardEntry(CurrencyId.Mola, molaAmount)
                }));

            if (itemEntries.Count > 0)
                rewardDefinitions.Add(new ItemRewardDefinition(itemEntries));

            if (rewardDefinitions.Count == 0)
                throw new InvalidOperationException(
                    $"[DialogueDemoResourceExecution] 当前资源包在 ItemDatabase 中没有可发放内容，resourceType={resourceType}。");

            // RewardSystem 在任何领域状态写入前准备全部 Handler，避免钱包或任一背包拒绝时部分到账。
            RewardGrantResult result = context.Architecture.GetSystem<RewardSystem>().TryGrant(rewardDefinitions);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"[DialogueDemoResourceExecution] 演示奖励发放失败，resourceType={resourceType}, " +
                    $"sessionId={context.Session.SessionId}, choiceNodeId={context.Choice.NodeId}, " +
                    $"failureDomain={result.FailureDomain}, currencyStatus={result.CurrencyStatus?.ToString() ?? "<none>"}, " +
                    $"currencyId={result.CurrencyId}, inventoryStatus={result.InventoryStatus?.ToString() ?? "<none>"}, " +
                    $"itemId={result.ItemId}。");
            }

            Debug.Log(
                $"[DialogueDemoResourceExecution] 演示奖励发放成功，resourceType={resourceType}, " +
                $"itemCount={itemEntries.Count}, molaAmount={molaAmount}, sessionId={context.Session.SessionId}, " +
                $"choiceNodeId={context.Choice.NodeId}。");
        }

        /// <summary>按所选资源包判断数据库物品分类是否应发放，并返回固定数量。</summary>
        /// <param name="category">数据库中物品的分类。</param>
        /// <param name="quantity">该分类物品在本资源包中的数量。</param>
        /// <returns>应纳入资源包时返回 true。</returns>
        private bool TryGetQuantity(ItemCategory category, out int quantity)
        {
            switch (resourceType)
            {
                case E_DialogueDemoResourceType.AllResources:
                    if (category == ItemCategory.Weapon || category == ItemCategory.Artifact)
                    {
                        quantity = 1;
                        return true;
                    }
                    if (category == ItemCategory.DevelopmentExperienceItem ||
                        category == ItemCategory.DevelopmentItem || category == ItemCategory.Food)
                    {
                        quantity = 10;
                        return true;
                    }
                    break;
                case E_DialogueDemoResourceType.Weapons when category == ItemCategory.Weapon:
                case E_DialogueDemoResourceType.Artifacts when category == ItemCategory.Artifact:
                    quantity = 1;
                    return true;
                case E_DialogueDemoResourceType.UpgradeMaterials
                    when category == ItemCategory.DevelopmentExperienceItem || category == ItemCategory.DevelopmentItem:
                    quantity = 10;
                    return true;
            }

            quantity = 0;
            return false;
        }

        #endregion
    }
}
