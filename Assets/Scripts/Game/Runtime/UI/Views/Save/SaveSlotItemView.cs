using System;
using RPG.Game.Saving;
using RPG.SaveSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Save
{
    /// <summary>显示一个固定卡位，转发选择与删除意图，不读取文件或业务 Manager。</summary>
    [DisallowMultipleComponent]
    public sealed class SaveSlotItemView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private Button selectButton;
        [SerializeField, Required] private Button deleteButton;
        [SerializeField, Required] private TMP_Text titleText;
        [SerializeField, Required] private TMP_Text statusText;
        [SerializeField, Required] private TMP_Text timeText;
        [SerializeField, Required] private GameObject partyRoot;
        [SerializeField, Required] private GameObject emptyRoot;
        [SerializeField, Required] private Image selectedBorder;
        [SerializeField, Required] private Image[] portraits = new Image[4];
        [SerializeField, Required] private TMP_Text[] characterNames = new TMP_Text[4];
        private Action<int> selectRequested;
        private Action<int> deleteRequested;
        private int slotIndex;
        #endregion

        #region 绑定与释放
        /// <summary>由列表创建后绑定一次回调，卡片不持有控制器以外的服务。</summary>
        /// <param name="index">固定零基卡位。</param>
        /// <param name="select">选档回调。</param>
        /// <param name="delete">删除请求回调。</param>
        public void Initialize(int index, Action<int> select, Action<int> delete)
        {
            slotIndex = index;
            selectRequested = select;
            deleteRequested = delete;
            selectButton.onClick.AddListener(HandleSelect);
            deleteButton.onClick.AddListener(HandleDelete);
        }

        /// <summary>销毁卡片时对称解除按钮事件。</summary>
        private void OnDestroy()
        {
            selectButton.onClick.RemoveListener(HandleSelect);
            deleteButton.onClick.RemoveListener(HandleDelete);
            selectRequested = null;
            deleteRequested = null;
        }

        /// <summary>转发当前卡位的选择意图。</summary>
        private void HandleSelect() => selectRequested?.Invoke(slotIndex);
        /// <summary>转发当前卡位的删除意图。</summary>
        private void HandleDelete() => deleteRequested?.Invoke(slotIndex);
        #endregion

        #region 显示
        /// <summary>覆盖卡片全部状态，头像异步加载后由独立入口更新。</summary>
        /// <param name="slot">卡位详情。</param>
        /// <param name="selected">是否选中。</param>
        /// <param name="interactable">是否可操作。</param>
        public void Refresh(GameSaveSlot slot, bool selected, bool interactable)
        {
            titleText.text = $"存档 {slot.Index + 1}";
            selectedBorder.enabled = selected;
            selectButton.interactable = interactable;
            deleteButton.gameObject.SetActive(!slot.IsEmpty);
            deleteButton.interactable = interactable;
            emptyRoot.SetActive(slot.IsEmpty);
            partyRoot.SetActive(!slot.IsEmpty);
            statusText.text = selected ? "已选择" : string.Empty;
            timeText.text = slot.IsEmpty ? "空存档" : "摘要读取失败";
            GameSaveSummary summary = null;
            if (!slot.IsEmpty && slot.Details.SummaryResult.IsSuccess)
            {
                SaveSlotSummary frameworkSummary = slot.Details.SummaryResult.Value;
                summary = frameworkSummary.BusinessSummary as GameSaveSummary;
                timeText.text = "最后保存 · " + DateTimeOffset.FromUnixTimeMilliseconds(frameworkSummary.SavedAtUtcUnixMilliseconds)
                    .ToLocalTime().ToString("yyyy/MM/dd HH:mm");
            }
            if (!slot.CanStart) statusText.text = "存档异常";
            for (int index = 0; index < 4; index++)
            {
                GameSavePartyMemberSummary member = summary?.PartyMembers[index];
                characterNames[index].text = member == null || string.IsNullOrEmpty(member.CharacterId) ? "空位" : member.CharacterName;
                portraits[index].sprite = null;
                portraits[index].enabled = false;
            }
        }

        /// <summary>仅接收当前列表版本加载成功的头像；缺失时保留空位底衬与名称。</summary>
        /// <param name="index">队伍槽位。</param>
        /// <param name="sprite">头像或空值。</param>
        public void RefreshPortrait(int index, Sprite sprite)
        {
            portraits[index].sprite = sprite;
            portraits[index].enabled = sprite != null;
        }
        #endregion
    }
}
