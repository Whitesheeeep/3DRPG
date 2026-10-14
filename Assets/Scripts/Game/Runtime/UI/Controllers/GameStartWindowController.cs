using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game.Saving;
using RPG.Game.UI.Services;
using UnityEngine;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>协调十档选择、确认删除、进入游戏与头像图集租约。</summary>
    public sealed class GameStartWindowController : MonoBehaviour
    {
        #region 依赖字段
        private GameStartWindowDataComponent data;
        private GameSaveSystem saveSystem;
        private WindowSpriteAtlasLeaseService atlases;
        #endregion
        #region 请求状态
        private bool busy;
        private int selectedIndex;
        private int deleteIndex = -1;
        private int refreshVersion;
        #endregion
        #region 生命周期
        /// <summary>接收显式引用，创建卡片并绑定删除确认。</summary>
        /// <param name="sourceData">窗口配置。</param>
        public void Initialize(GameStartWindowDataComponent sourceData)
        {
            data = sourceData;
            saveSystem = GameArchitecture.Interface.GetSystem<GameSaveSystem>();
            data.TitleText.text = "探索与冒险";
            data.StartButtonText.text = "开始游戏";
            data.DeletePanel.SetActive(false);
            data.SlotList.Initialize(SelectSlot, RequestDelete);
            data.ConfirmDeleteButton.onClick.AddListener(ConfirmDelete);
            data.CancelDeleteButton.onClick.AddListener(CancelDelete);
            WSLog.Log("[GameStartWindowController] 选档列表及删除确认已绑定。");
        }
        /// <summary>每次显示重新读取磁盘摘要。</summary>
        public void ResetForOpen() => RefreshOpenAsync().Forget(HandleFailure);
        /// <summary>隐藏使未完成头像结果失效并释放图集。</summary>
        public void HandleHidden()
        {
            refreshVersion++;
            atlases?.Dispose();
            atlases = null;
            WSLog.Log("[GameStartWindowController] 已释放头像图集。");
        }
        /// <summary>销毁时解除事件和图集引用。</summary>
        public void Dispose()
        {
            HandleHidden();
            data.ConfirmDeleteButton.onClick.RemoveListener(ConfirmDelete);
            data.CancelDeleteButton.onClick.RemoveListener(CancelDelete);
            data = null;
        }
        #endregion
        #region 用户操作
        /// <summary>只选择卡位，不立即新建或读档。</summary>
        /// <param name="index">零基卡位。</param>
        private void SelectSlot(int index)
        {
            if (busy || deleteIndex >= 0) return;
            selectedIndex = index;
            RefreshCards();
            ApplyPortraits();
        }
        /// <summary>提交选定卡位的开始请求。</summary>
        public void RequestStart()
        {
            if (busy || deleteIndex >= 0) return;
            StartSelectedAsync().Forget(HandleFailure);
        }
        /// <summary>显示确认，不立即删除文件。</summary>
        /// <param name="index">零基卡位。</param>
        private void RequestDelete(int index)
        {
            if (busy) return;
            deleteIndex = index;
            data.DeletePrompt.text = $"删除存档 {index + 1}？\n此存档进度将被删除，无法恢复。";
            data.DeletePanel.SetActive(true);
            RefreshCards();
        }
        /// <summary>取消删除并恢复卡片操作。</summary>
        private void CancelDelete()
        {
            deleteIndex = -1;
            data.DeletePanel.SetActive(false);
            RefreshCards();
            ApplyPortraits();
        }
        /// <summary>确认后删除并刷新。</summary>
        private void ConfirmDelete()
        {
            if (!busy && deleteIndex >= 0) DeleteSelectedAsync().Forget(HandleFailure);
        }
        #endregion
        #region 异步操作
        /// <summary>刷新摘要，选择最近保存的可用档，再统一加载头像。</summary>
        /// <returns>列表与头像请求完成。</returns>
        private async UniTask RefreshOpenAsync()
        {
            busy = true;
            data.StartButton.interactable = false;
            data.StatusText.text = "正在读取存档…";
            await saveSystem.RefreshSlotsAsync();
            selectedIndex = 0;
            long latest = -1;
            foreach (GameSaveSlot slot in saveSystem.Slots)
                if (!slot.IsEmpty && slot.CanStart && slot.Details.SummaryResult.Value.SavedAtUtcUnixMilliseconds > latest)
                {
                    latest = slot.Details.SummaryResult.Value.SavedAtUtcUnixMilliseconds;
                    selectedIndex = slot.Index;
                }
            if (latest < 0)
                foreach (GameSaveSlot slot in saveSystem.Slots) if (slot.IsEmpty) { selectedIndex = slot.Index; break; }
            busy = false;
            deleteIndex = -1;
            data.DeletePanel.SetActive(false);
            data.StatusText.text = string.Empty;
            RefreshCards();
            data.SlotList.Reveal(selectedIndex);
            await LoadPortraitsAsync();
        }
        /// <summary>进入期间禁止交叉创建会话。</summary>
        /// <returns>场景请求完成。</returns>
        private async UniTask StartSelectedAsync()
        {
            busy = true;
            data.StatusText.text = "正在准备游戏…";
            RefreshCards();
            await saveSystem.StartSlotAsync(selectedIndex, data.TargetSceneConfig);
        }
        /// <summary>删除目标文件，保持所有卡位顺序。</summary>
        /// <returns>删除及图集刷新完成。</returns>
        private async UniTask DeleteSelectedAsync()
        {
            int index = deleteIndex;
            busy = true;
            data.ConfirmDeleteButton.interactable = false;
            data.CancelDeleteButton.interactable = false;
            await saveSystem.DeleteSlotAsync(index);
            busy = false;
            data.ConfirmDeleteButton.interactable = true;
            data.CancelDeleteButton.interactable = true;
            CancelDelete();
            data.StatusText.text = $"存档 {index + 1} 已删除。";
            await LoadPortraitsAsync();
        }
        /// <summary>整窗复用去重图集，迟到结果须匹配刷新版本才能回写。</summary>
        /// <returns>图集加载完成。</returns>
        private async UniTask LoadPortraitsAsync()
        {
            int version = ++refreshVersion;
            atlases?.Dispose();
            var addresses = new List<string>();
            foreach (GameSaveSlot slot in saveSystem.Slots)
            {
                if (slot.IsEmpty || !slot.CanStart) continue;
                var summary = (GameSaveSummary)slot.Details.SummaryResult.Value.BusinessSummary;
                foreach (GameSavePartyMemberSummary member in summary.PartyMembers)
                    if (!string.IsNullOrEmpty(member.CharacterId) && CharacterConfigManager.Instance.TryGetConfig(new CharacterId(member.CharacterId), out CharacterConfig config))
                        addresses.Add(config.AvatarAddress);
            }
            var lease = new WindowSpriteAtlasLeaseService(addresses, 0f, "GameStartWindow");
            atlases = lease;
            await lease.BeginLoadConfiguredAtlasesAsync();
            if (version == refreshVersion && data != null) ApplyPortraits();
        }
        #endregion
        #region 显示辅助
        /// <summary>刷新卡片及门禁，不读取文件。</summary>
        private void RefreshCards()
        {
            bool available = !busy && deleteIndex < 0 && saveSystem.State == E_GameSaveSessionState.Title;
            data.SlotList.Refresh(saveSystem.Slots, selectedIndex, available);
            data.StartButton.interactable = available && saveSystem.Slots.Count == 10 && saveSystem.Slots[selectedIndex].CanStart;
            if (saveSystem.Slots.Count == 10)
                data.SelectionText.text = $"已选择存档 {selectedIndex + 1} · " + (saveSystem.Slots[selectedIndex].IsEmpty ? "开始新的冒险" : "继续冒险");
        }
        /// <summary>根据当前摘要身份填入已缓存头像，失败保留名字及占位。</summary>
        private void ApplyPortraits()
        {
            if (atlases == null) return;
            foreach (GameSaveSlot slot in saveSystem.Slots)
            {
                if (slot.IsEmpty || !slot.CanStart) continue;
                var summary = (GameSaveSummary)slot.Details.SummaryResult.Value.BusinessSummary;
                for (int index = 0; index < 4; index++)
                {
                    string id = summary.PartyMembers[index].CharacterId;
                    if (string.IsNullOrEmpty(id) || !CharacterConfigManager.Instance.TryGetConfig(new CharacterId(id), out CharacterConfig config)) continue;
                    atlases.TryGetSprite(config.AvatarAddress, config.AvatarSpriteName, out Sprite sprite);
                    data.SlotList.RefreshPortrait(slot.Index, index, sprite);
                }
            }
        }
        /// <summary>显示错误，仅在业务允许时恢复开始按钮。</summary>
        /// <param name="exception">文件、业务或场景异常。</param>
        private void HandleFailure(Exception exception)
        {
            busy = false;
            if (data == null) return;
            data.StatusText.text = exception.Message;
            data.ConfirmDeleteButton.interactable = true;
            data.CancelDeleteButton.interactable = true;
            RefreshCards();
            ApplyPortraits();
            WSLog.LogError($"[GameStartWindowController] 选档操作失败，exception={exception}");
        }
        #endregion
    }
}
