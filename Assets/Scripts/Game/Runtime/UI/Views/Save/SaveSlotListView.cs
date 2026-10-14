using System;
using System.Collections.Generic;
using RPG.Game.Saving;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Views.Save
{
    /// <summary>拥有十张卡片实例及其横向布局生命周期。</summary>
    public sealed class SaveSlotListView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private ScrollRect scrollRect;
        [SerializeField, Required] private RectTransform content;
        [SerializeField, Required] private SaveSlotItemView itemPrefab;
        private readonly List<SaveSlotItemView> items = new List<SaveSlotItemView>();
        #endregion

        #region 列表操作
        /// <summary>为本窗口一次创建十张卡片，Transform 使用 UI 局部坐标。</summary>
        /// <param name="select">选中卡位意图。</param>
        /// <param name="delete">删除卡位意图。</param>
        public void Initialize(Action<int> select, Action<int> delete)
        {
            for (int index = 0; index < 10; index++)
            {
                SaveSlotItemView item = Instantiate(itemPrefab, content, false);
                item.Initialize(index, select, delete);
                items.Add(item);
            }
            WSLog.Log("[SaveSlotListView] 已创建十张存档卡片。");
        }

        /// <summary>刷新固定卡位，无须重建实例或重新绑定按钮。</summary>
        /// <param name="slots">十个卡位。</param>
        /// <param name="selected">选中卡位。</param>
        /// <param name="interactable">当前是否允许操作。</param>
        public void Refresh(IReadOnlyList<GameSaveSlot> slots, int selected, bool interactable)
        {
            for (int index = 0; index < slots.Count; index++) items[index].Refresh(slots[index], index == selected, interactable);
        }

        /// <summary>将异步图集结果写入指定卡片和队伍槽。</summary>
        /// <param name="slot">卡位。</param>
        /// <param name="member">队伍槽。</param>
        /// <param name="sprite">头像。</param>
        public void RefreshPortrait(int slot, int member, Sprite sprite) => items[slot].RefreshPortrait(member, sprite);

        /// <summary>将初始选中卡位滚动到可见区域，运行在布局计算完成后。</summary>
        /// <param name="index">选中卡位。</param>
        public void Reveal(int index)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.horizontalNormalizedPosition = index / 9f;
        }
        #endregion
    }
}
