using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.TAG;

namespace RPG.Game.UI.Config
{
    /// <summary>为 HUD 的 Gameplay Effect GrantedTag 配置对应的展示 Sprite。</summary>
    [CreateAssetMenu(fileName = "HUDGEIconConfig", menuName = "RPG/UI/HUD GE Icon Config")]
    public sealed class HUDGEIconConfig : ScriptableObject
    {
        #region 配置字段

        [SerializeField, LabelText("GrantedTag 图标映射")]
        private List<HUDGEIconEntry> entries = new();

        #endregion

        #region 配置查询

        /// <summary>获取按作者顺序排列的 Tag 与 Sprite 配置。</summary>
        public IReadOnlyList<HUDGEIconEntry> Entries => entries;

        /// <summary>验证映射 Tag 均存在于数据库、互不重复且配置了 Sprite。</summary>
        /// <exception cref="InvalidOperationException">存在无效、重复或缺少图片的配置时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (entries == null)
                throw new InvalidOperationException($"[HUDGEIconConfig] 配置资产 '{name}' 的映射列表为空引用。");
            if (entries.Count == 0) return;

            if (!GameplayTagManager.Instance.IsInitialized)
                throw new InvalidOperationException("[HUDGEIconConfig] GameplayTagManager 尚未初始化，无法校验 HUD GE 图标映射。");

            var configuredTags = new HashSet<GameplayTag>();
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                HUDGEIconEntry entry = entries[entryIndex];
                if (!GameplayTagManager.Instance.IsValidTag(entry.Tag))
                    throw new InvalidOperationException(
                        $"[HUDGEIconConfig] 资产 '{name}' 第 {entryIndex + 1} 项包含无效 GrantedTag，tagId={entry.Tag.Id}。");
                if (!configuredTags.Add(entry.Tag))
                    throw new InvalidOperationException(
                        $"[HUDGEIconConfig] 资产 '{name}' 中 Tag {entry.Tag} 被重复配置。");
                if (entry.Icon == null)
                    throw new InvalidOperationException(
                        $"[HUDGEIconConfig] 资产 '{name}' 中 Tag {entry.Tag} 未配置 Sprite。");
            }
        }

        /// <summary>精确查询 GrantedTag 对应的图标，不执行层级 Tag 匹配。</summary>
        /// <param name="tag">GE Runtime 实际授予的 Tag。</param>
        /// <param name="icon">找到映射时返回的 Sprite。</param>
        /// <returns>存在精确 Tag 映射时返回 true。</returns>
        public bool TryGetIcon(GameplayTag tag, out Sprite icon)
        {
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                HUDGEIconEntry entry = entries[entryIndex];
                if (entry.Tag != tag) continue;
                icon = entry.Icon;
                return icon != null;
            }

            icon = null;
            return false;
        }

        #endregion
    }

    /// <summary>保存一个 GrantedTag 与 HUD 图标 Sprite 的精确映射。</summary>
    [Serializable]
    public struct HUDGEIconEntry
    {
        #region 配置字段

        [SerializeField, LabelText("Granted Tag")]
        private GameplayTag tag;
        [SerializeField, Required, LabelText("Sprite")]
        private Sprite icon;

        #endregion

        #region 属性

        /// <summary>获取该映射使用的 GrantedTag。</summary>
        public GameplayTag Tag => tag;

        /// <summary>获取该映射使用的 Sprite。</summary>
        public Sprite Icon => icon;

        #endregion
    }
}
