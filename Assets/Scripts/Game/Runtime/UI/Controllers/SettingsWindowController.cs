using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game.Saving;
using RPG.Game.Settings;
using RPG.Game.UI.Services;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>连接音量偏好、游戏保存和设定控件，管理异步请求及头像租约。</summary>
    public sealed class SettingsWindowController : MonoBehaviour
    {
        #region 依赖字段
        private SettingsWindowDataComponent data;
        private GameSaveSystem saves;
        private GameSettingsManager settings;
        private WindowSpriteAtlasLeaseService atlases;
        private bool requestRunning;
        #endregion

        #region 生命周期
        /// <summary>对称绑定业务变化、按钮与滑条。</summary>
        /// <param name="source">Prefab 显式引用。</param>
        public void Initialize(SettingsWindowDataComponent source)
        {
            data = source;
            saves = GameArchitecture.Interface.GetSystem<GameSaveSystem>();
            settings = GameArchitecture.Interface.GetManager<GameSettingsManager>();
            saves.Changed += RefreshState;
            data.SaveButton.onClick.AddListener(RequestSave);
            data.ReturnTitleButton.onClick.AddListener(RequestReturn);
            data.CloseButton.onClick.AddListener(RequestClose);
            data.ContinueButton.onClick.AddListener(RequestClose);
            data.MasterSlider.onValueChanged.AddListener(HandleMasterChanged);
            data.MusicSlider.onValueChanged.AddListener(HandleMusicChanged);
            data.EffectSlider.onValueChanged.AddListener(HandleEffectChanged);
            WSLog.Log("[SettingsWindowController] 已连接设定控件与业务状态。");
        }

        /// <summary>显示时读取当前偏好及摘要，不预热窗口或图集。</summary>
        public void HandleShown()
        {
            data.MasterSlider.SetValueWithoutNotify(settings.MasterVolume);
            data.MusicSlider.SetValueWithoutNotify(settings.MusicVolume);
            data.EffectSlider.SetValueWithoutNotify(settings.EffectVolume);
            RefreshVolumeLabels();
            RefreshState();
            LoadPortraitsAsync().Forget(HandleFailure);
        }

        /// <summary>销毁时解除所有订阅，清理图集并提交音量偏好。</summary>
        public void Dispose()
        {
            saves.Changed -= RefreshState;
            data.SaveButton.onClick.RemoveListener(RequestSave);
            data.ReturnTitleButton.onClick.RemoveListener(RequestReturn);
            data.CloseButton.onClick.RemoveListener(RequestClose);
            data.ContinueButton.onClick.RemoveListener(RequestClose);
            data.MasterSlider.onValueChanged.RemoveListener(HandleMasterChanged);
            data.MusicSlider.onValueChanged.RemoveListener(HandleMusicChanged);
            data.EffectSlider.onValueChanged.RemoveListener(HandleEffectChanged);
            foreach (var image in data.Portraits) image.sprite = null;
            atlases?.Dispose();
            settings.SavePreferences();
            data = null;
            WSLog.Log("[SettingsWindowController] 已解绑并释放窗口资源。");
        }
        #endregion

        #region 用户请求
        /// <summary>拒绝重复保存，交给统一业务入口。</summary>
        private void RequestSave()
        {
            if (!requestRunning) SaveAsync().Forget(HandleFailure);
        }
        /// <summary>保存成功才开始返回主界面。</summary>
        private void RequestReturn()
        {
            if (!requestRunning) ReturnAsync().Forget(HandleFailure);
        }
        /// <summary>关闭必须等待当前保存结束，隐藏后再销毁。</summary>
        public void RequestClose()
        {
            if (!requestRunning && !saves.IsSaving && saves.State != E_GameSaveSessionState.Returning)
                CloseAsync().Forget(HandleFailure);
        }
        /// <summary>等待手动保存并恢复控件交互。</summary>
        /// <returns>保存完成。</returns>
        private async UniTask SaveAsync()
        {
            requestRunning = true;
            try { RefreshState(); await saves.SaveCurrentAsync(); }
            finally { requestRunning = false; if (data != null) RefreshState(); }
        }
        /// <summary>等待保存和返回流程；窗口可能由清理 Task 销毁。</summary>
        /// <returns>返回请求完成。</returns>
        private async UniTask ReturnAsync()
        {
            requestRunning = true;
            try { RefreshState(); await saves.ReturnToTitleAsync(); }
            finally { requestRunning = false; if (data != null) RefreshState(); }
        }
        /// <summary>先保存偏好，再隐藏并释放 Addressables 窗口资源。</summary>
        /// <returns>关闭流程完成。</returns>
        private async UniTask CloseAsync()
        {
            requestRunning = true;
            settings.SavePreferences();
            await UIManager.Instance.HideWindowAsync<SettingsWindow>();
            UIManager.Instance.DestroyWindow<SettingsWindow>();
        }
        #endregion

        #region 显示与资源
        /// <summary>业务状态是保存交互与反馈的唯一来源。</summary>
        private void RefreshState()
        {
            bool enabled = !requestRunning && !saves.IsSaving && saves.State == E_GameSaveSessionState.Playing;
            data.CurrentSlotText.text = saves.CurrentSlotNumber > 0 ? $"当前存档 {saves.CurrentSlotNumber}" : "调试会话 · 未选择存档";
            data.StatusText.text = saves.StatusMessage;
            data.LastSaveText.text = saves.LastSavedAtUtcMilliseconds > 0
                ? "上次保存 · " + DateTimeOffset.FromUnixTimeMilliseconds(saves.LastSavedAtUtcMilliseconds).ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                : "尚未保存";
            data.SaveButton.interactable = enabled && saves.CurrentSlotNumber > 0;
            data.ReturnTitleButton.interactable = enabled && saves.CurrentSlotNumber > 0;
            data.CloseButton.interactable = enabled;
            data.ContinueButton.interactable = enabled;
        }

        /// <summary>按当前队伍加载图集；销毁后的迟到结果只完成服务自身释放。</summary>
        /// <returns>头像请求结束。</returns>
        private async UniTask LoadPortraitsAsync()
        {
            CharacterPartyManager party = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
            var configs = new CharacterConfig[4];
            var addresses = new List<string>();
            for (int index = 0; index < 4; index++)
            {
                data.Portraits[index].enabled = false;
                CharacterId id = party.GetCharacterIdAtSlot(index);
                if (id.IsValid) { configs[index] = CharacterConfigManager.Instance.GetRequiredConfig(id); addresses.Add(configs[index].AvatarAddress); }
            }
            atlases = new WindowSpriteAtlasLeaseService(addresses, 0f, "SettingsWindow");
            await atlases.BeginLoadConfiguredAtlasesAsync();
            if (data == null) return;
            for (int index = 0; index < 4; index++)
                if (configs[index] != null && atlases.TryGetSprite(configs[index].AvatarAddress, configs[index].AvatarSpriteName, out Sprite sprite))
                { data.Portraits[index].sprite = sprite; data.Portraits[index].enabled = true; }
        }

        /// <summary>实时写入主音量。</summary>
        /// <param name="value">零到一音量。</param>
        private void HandleMasterChanged(float value) { settings.SetMasterVolume(value); RefreshVolumeLabels(); }
        /// <summary>实时写入音乐音量。</summary>
        /// <param name="value">零到一音量。</param>
        private void HandleMusicChanged(float value) { settings.SetMusicVolume(value); RefreshVolumeLabels(); }
        /// <summary>实时写入音效音量。</summary>
        /// <param name="value">零到一音量。</param>
        private void HandleEffectChanged(float value) { settings.SetEffectVolume(value); RefreshVolumeLabels(); }
        /// <summary>刷新三个滑条的百分比，不产生高频日志。</summary>
        private void RefreshVolumeLabels()
        {
            data.MasterValueText.text = $"{settings.MasterVolume:P0}";
            data.MusicValueText.text = $"{settings.MusicVolume:P0}";
            data.EffectValueText.text = $"{settings.EffectVolume:P0}";
        }
        /// <summary>将异步失败显示在仍存在的窗口中并记录上下文。</summary>
        /// <param name="exception">请求异常。</param>
        private void HandleFailure(Exception exception)
        {
            requestRunning = false;
            if (data != null) { RefreshState(); data.StatusText.text = exception.Message; }
            WSLog.LogError($"[SettingsWindowController] 操作失败，exception={exception}");
        }
        #endregion
    }
}
