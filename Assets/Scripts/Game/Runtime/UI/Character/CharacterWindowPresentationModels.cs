using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.Game.UI.Character
{
    /// <summary>角色窗口当前帧所需的完整显示快照。</summary>
    public sealed class CharacterWindowViewData
    {
        /// <summary>创建角色窗口显示快照。</summary>
        /// <param name="roster">顶部角色条目。</param>
        /// <param name="selectedIndex">当前选中条目下标。</param>
        /// <param name="header">公共标题数据。</param>
        /// <param name="fullBodyPortrait">中央全身立绘。</param>
        /// <param name="artifactSummary">全部已装备圣遗物的属性汇总。</param>
        /// <param name="page">当前页面。</param>
        /// <param name="attributes">角色等级基础值与静态装备效果结算后的 Stat 总值行。</param>
        /// <param name="weapon">武器页面数据。</param>
        /// <param name="artifacts">五个圣遗物槽位。</param>
        /// <param name="selectedArtifactIndex">当前选中的圣遗物槽位。</param>
        /// <param name="selectedArtifact">当前选中的圣遗物详情。</param>
        public CharacterWindowViewData(IReadOnlyList<CharacterRosterEntryViewData> roster, int selectedIndex,
            CharacterHeaderViewData header, Sprite fullBodyPortrait, CharacterArtifactSummaryViewData artifactSummary,
            CharacterWindowPage page,
            IReadOnlyList<CharacterAttributeViewData> attributes, CharacterWeaponViewData weapon,
            IReadOnlyList<CharacterArtifactSlotItemViewData> artifacts, int selectedArtifactIndex,
            CharacterArtifactViewData selectedArtifact)
        {
            Roster = roster ?? Array.Empty<CharacterRosterEntryViewData>();
            SelectedIndex = selectedIndex;
            Header = header;
            FullBodyPortrait = fullBodyPortrait;
            ArtifactSummary = artifactSummary;
            Page = page;
            Attributes = attributes ?? Array.Empty<CharacterAttributeViewData>();
            Weapon = weapon;
            Artifacts = artifacts ?? Array.Empty<CharacterArtifactSlotItemViewData>();
            SelectedArtifactIndex = selectedArtifactIndex;
            SelectedArtifact = selectedArtifact;
        }

        /// <summary>获取角色条目。</summary>
        public IReadOnlyList<CharacterRosterEntryViewData> Roster { get; }
        /// <summary>获取选中条目下标。</summary>
        public int SelectedIndex { get; }
        /// <summary>获取公共标题。</summary>
        public CharacterHeaderViewData Header { get; }
        /// <summary>获取中央全身立绘；对应 Sprite 尚未导入时为 null。</summary>
        public Sprite FullBodyPortrait { get; }
        /// <summary>获取全部已装备圣遗物的静态属性汇总。</summary>
        public CharacterArtifactSummaryViewData ArtifactSummary { get; }
        /// <summary>获取当前页面。</summary>
        public CharacterWindowPage Page { get; }
        /// <summary>获取角色等级基础值与静态装备效果结算后的 Stat 总值行。</summary>
        public IReadOnlyList<CharacterAttributeViewData> Attributes { get; }
        /// <summary>获取武器页面数据。</summary>
        public CharacterWeaponViewData Weapon { get; }
        /// <summary>获取圣遗物槽位。</summary>
        public IReadOnlyList<CharacterArtifactSlotItemViewData> Artifacts { get; }
        /// <summary>获取当前圣遗物槽位下标。</summary>
        public int SelectedArtifactIndex { get; }
        /// <summary>获取当前选中的圣遗物详情。</summary>
        public CharacterArtifactViewData SelectedArtifact { get; }
    }

    /// <summary>顶部角色头像条目的显示数据。</summary>
    public sealed class CharacterRosterEntryViewData
    {
        /// <summary>创建角色头像条目。</summary>
        /// <param name="instance">角色实例。</param>
        /// <param name="sideIcon">侧面头像。</param>
        /// <param name="selected">是否选中。</param>
        /// <param name="active">是否为当前战斗角色。</param>
        public CharacterRosterEntryViewData(CharacterInstance instance, Sprite sideIcon, bool selected, bool active,
            bool inActiveParty, int partySlotIndex, Sprite partyMarkSprite)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            SideIcon = sideIcon;
            Selected = selected;
            Active = active;
            IsInActiveParty = inActiveParty;
            PartySlotIndex = partySlotIndex;
            PartyMarkSprite = partyMarkSprite;
        }

        /// <summary>获取稳定角色实例。</summary>
        public CharacterInstance Instance { get; }
        /// <summary>获取侧面头像。</summary>
        public Sprite SideIcon { get; }
        /// <summary>获取稀有度。</summary>
        public int Rarity => (int)Instance.Config.Rarity;
        /// <summary>获取是否选中。</summary>
        public bool Selected { get; }
        /// <summary>获取是否为当前战斗角色。</summary>
        public bool Active { get; }
        /// <summary>获取角色是否属于当前唯一队伍。</summary>
        public bool IsInActiveParty { get; }
        /// <summary>获取队伍槽位；不在队伍时为负数。</summary>
        public int PartySlotIndex { get; }
        /// <summary>获取队伍槽位标记图。</summary>
        public Sprite PartyMarkSprite { get; }
    }

    /// <summary>角色窗口公共标题数据。</summary>
    public sealed class CharacterHeaderViewData
    {
        /// <summary>创建标题数据。</summary>
        /// <param name="name">角色名称。</param>
        /// <param name="rarity">稀有度。</param>
        /// <param name="levelText">等级文本。</param>
        /// <param name="experienceText">经验文本。</param>
        /// <param name="capStateText">等级上限状态。</param>
        /// <param name="experienceProgress">当前等级内经验百分比。</param>
        /// <param name="showExperienceProgress">是否显示经验条。</param>
        public CharacterHeaderViewData(string name, int rarity, string levelText, string experienceText,
            string capStateText, float experienceProgress, bool showExperienceProgress)
        {
            Name = name ?? string.Empty;
            Rarity = rarity;
            LevelText = levelText ?? string.Empty;
            ExperienceText = experienceText ?? string.Empty;
            CapStateText = capStateText ?? string.Empty;
            ExperienceProgress = Mathf.Clamp01(experienceProgress);
            ShowExperienceProgress = showExperienceProgress;
        }

        /// <summary>获取角色名称。</summary>
        public string Name { get; }
        /// <summary>获取稀有度。</summary>
        public int Rarity { get; }
        /// <summary>获取等级文本。</summary>
        public string LevelText { get; }
        /// <summary>获取经验文本。</summary>
        public string ExperienceText { get; }
        /// <summary>获取等级上限状态。</summary>
        public string CapStateText { get; }
        /// <summary>获取当前等级内经验进度。</summary>
        public float ExperienceProgress { get; }
        /// <summary>判断是否显示当前等级经验条。</summary>
        public bool ShowExperienceProgress { get; }
    }

    /// <summary>角色静态装备投影后的 Stat 总值显示行。</summary>
    public sealed class CharacterAttributeViewData
    {
        /// <summary>创建角色 Stat 总值显示行。</summary>
        /// <param name="attributeName">属性名称。</param>
        /// <param name="baseValueText">角色自身基础值文本。</param>
        /// <param name="equipmentBonusText">静态装备净加成文本；为零时为空字符串。</param>
        public CharacterAttributeViewData(string attributeName, string baseValueText, string equipmentBonusText)
        {
            AttributeName = attributeName ?? string.Empty;
            BaseValueText = baseValueText ?? string.Empty;
            EquipmentBonusText = equipmentBonusText ?? string.Empty;
        }

        /// <summary>获取属性名称。</summary>
        public string AttributeName { get; }
        /// <summary>获取角色自身基础值文本。</summary>
        public string BaseValueText { get; }
        /// <summary>获取静态装备净加成文本。</summary>
        public string EquipmentBonusText { get; }
    }

    /// <summary>已装备武器页面显示数据。</summary>
    public sealed class CharacterWeaponViewData
    {
        /// <summary>创建武器页面数据。</summary>
        /// <param name="hasWeapon">是否存在已装备武器。</param>
        /// <param name="instanceId">武器实例标识。</param>
        /// <param name="icon">武器图标。</param>
        /// <param name="name">武器名称。</param>
        /// <param name="type">武器类型。</param>
        /// <param name="rarity">稀有度。</param>
        /// <param name="levelText">等级文本。</param>
        /// <param name="refinementText">精炼文本。</param>
        /// <param name="detailLines">武器属性和描述行。</param>
        /// <param name="description">武器定义描述。</param>
        public CharacterWeaponViewData(bool hasWeapon, EquipmentInstanceId instanceId, Sprite icon, string name,
            string type, int rarity, string levelText, string refinementText,
            IReadOnlyList<string> detailLines, string description)
        {
            HasWeapon = hasWeapon;
            InstanceId = instanceId;
            Icon = icon;
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Rarity = rarity;
            LevelText = levelText ?? string.Empty;
            RefinementText = refinementText ?? string.Empty;
            DetailLines = detailLines ?? Array.Empty<string>();
            Description = description ?? string.Empty;
        }

        /// <summary>获取是否有武器。</summary>
        public bool HasWeapon { get; }
        /// <summary>获取武器实例标识。</summary>
        public EquipmentInstanceId InstanceId { get; }
        /// <summary>获取武器图标。</summary>
        public Sprite Icon { get; }
        /// <summary>获取名称。</summary>
        public string Name { get; }
        /// <summary>获取武器类型。</summary>
        public string Type { get; }
        /// <summary>获取稀有度。</summary>
        public int Rarity { get; }
        /// <summary>获取等级。</summary>
        public string LevelText { get; }
        /// <summary>获取精炼。</summary>
        public string RefinementText { get; }
        /// <summary>获取详情行。</summary>
        public IReadOnlyList<string> DetailLines { get; }
        /// <summary>获取武器定义描述。</summary>
        public string Description { get; }
    }

    /// <summary>五件已装备圣遗物静态属性的汇总显示数据。</summary>
    public sealed class CharacterArtifactSummaryViewData
    {
        /// <summary>创建圣遗物属性汇总数据。</summary>
        /// <param name="equippedCount">已装备圣遗物数量。</param>
        /// <param name="attributeLines">合并后的属性行。</param>
        public CharacterArtifactSummaryViewData(int equippedCount, IReadOnlyList<string> attributeLines)
        {
            EquippedCount = equippedCount;
            AttributeLines = attributeLines ?? Array.Empty<string>();
        }

        /// <summary>获取已装备圣遗物数量。</summary>
        public int EquippedCount { get; }
        /// <summary>获取静态属性汇总行。</summary>
        public IReadOnlyList<string> AttributeLines { get; }
    }

    /// <summary>圣遗物槽位显示数据。</summary>
    public sealed class CharacterArtifactSlotItemViewData
    {
        /// <summary>创建圣遗物槽位数据。</summary>
        /// <param name="slot">部位。</param>
        /// <param name="slotName">部位名称。</param>
        /// <param name="hasArtifact">是否已装备。</param>
        /// <param name="instanceId">圣遗物实例标识。</param>
        /// <param name="icon">圣遗物图标。</param>
        /// <param name="rarity">稀有度。</param>
        /// <param name="levelText">等级文本。</param>
        /// <param name="selected">是否选中。</param>
        public CharacterArtifactSlotItemViewData(ArtifactSlot slot, string slotName, bool hasArtifact,
            EquipmentInstanceId instanceId, Sprite icon, int rarity, string levelText, bool selected)
        {
            Slot = slot;
            SlotName = slotName ?? string.Empty;
            HasArtifact = hasArtifact;
            InstanceId = instanceId;
            Icon = icon;
            Rarity = rarity;
            LevelText = levelText ?? string.Empty;
            Selected = selected;
        }

        /// <summary>获取部位。</summary>
        public ArtifactSlot Slot { get; }
        /// <summary>获取部位名称。</summary>
        public string SlotName { get; }
        /// <summary>获取是否装备。</summary>
        public bool HasArtifact { get; }
        /// <summary>获取实例标识。</summary>
        public EquipmentInstanceId InstanceId { get; }
        /// <summary>获取图标。</summary>
        public Sprite Icon { get; }
        /// <summary>获取稀有度。</summary>
        public int Rarity { get; }
        /// <summary>获取等级文本。</summary>
        public string LevelText { get; }
        /// <summary>获取是否选中。</summary>
        public bool Selected { get; }
    }

    /// <summary>当前选中圣遗物的详情数据。</summary>
    public sealed class CharacterArtifactViewData
    {
        /// <summary>创建圣遗物详情数据。</summary>
        /// <param name="hasArtifact">是否存在圣遗物。</param>
        /// <param name="name">名称。</param>
        /// <param name="slotName">部位名称。</param>
        /// <param name="rarity">稀有度。</param>
        /// <param name="icon">圣遗物图标。</param>
        /// <param name="levelText">等级文本。</param>
        /// <param name="detailLines">静态属性行。</param>
        /// <param name="description">描述。</param>
        /// <param name="instanceId">实例标识。</param>
        public CharacterArtifactViewData(bool hasArtifact, string name, string slotName, int rarity, Sprite icon,
            string levelText, IReadOnlyList<string> detailLines, string description, EquipmentInstanceId instanceId)
        {
            HasArtifact = hasArtifact;
            Name = name ?? string.Empty;
            SlotName = slotName ?? string.Empty;
            Rarity = rarity;
            Icon = icon;
            LevelText = levelText ?? string.Empty;
            DetailLines = detailLines ?? Array.Empty<string>();
            Description = description ?? string.Empty;
            InstanceId = instanceId;
        }

        /// <summary>获取是否有圣遗物。</summary>
        public bool HasArtifact { get; }
        /// <summary>获取名称。</summary>
        public string Name { get; }
        /// <summary>获取部位。</summary>
        public string SlotName { get; }
        /// <summary>获取稀有度。</summary>
        public int Rarity { get; }
        /// <summary>获取圣遗物图标。</summary>
        public Sprite Icon { get; }
        /// <summary>获取等级文本。</summary>
        public string LevelText { get; }
        /// <summary>获取静态属性行。</summary>
        public IReadOnlyList<string> DetailLines { get; }
        /// <summary>获取描述。</summary>
        public string Description { get; }
        /// <summary>获取实例标识。</summary>
        public EquipmentInstanceId InstanceId { get; }
    }
}
