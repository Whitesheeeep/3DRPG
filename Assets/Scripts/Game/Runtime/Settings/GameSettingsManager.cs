using UnityEngine;
using WS_Modules.AudioSystem;
using WS_Modules.BusinessArchitecture;
using WS_Modules.LogModule;

namespace RPG.Game.Settings
{
    /// <summary>保存全局音量偏好，不参与游戏存档和会话重置。</summary>
    public sealed class GameSettingsManager : AbstractManager
    {
        #region 偏好键
        private const string MasterKey = "RPG.Settings.MasterVolume";
        private const string MusicKey = "RPG.Settings.MusicVolume";
        private const string EffectKey = "RPG.Settings.EffectVolume";
        #endregion

        #region 查询
        /// <summary>获取主音量。</summary>
        public float MasterVolume => AudioManager.Instance.GlobalVolume;
        /// <summary>获取音乐音量。</summary>
        public float MusicVolume => AudioManager.Instance.BGVolume;
        /// <summary>获取音效音量。</summary>
        public float EffectVolume => AudioManager.Instance.EffectVolume;
        #endregion

        #region 生命周期
        /// <summary>框架音频初始化后读取偏好；Mixer 的首次应用仍由框架 Start 完成。</summary>
        protected override void OnInit()
        {
            AudioManager.Instance.GlobalVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
            AudioManager.Instance.BGVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));
            AudioManager.Instance.EffectVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectKey, 1f));
            WSLog.Log("[GameSettingsManager] 已读取全局音量偏好。");
        }

        /// <summary>架构注销时提交尚未写入磁盘的偏好。</summary>
        protected override void OnDeinit() => SavePreferences();
        #endregion

        #region 音量操作
        /// <summary>实时更新主音量，只更新内存偏好，不在滑动过程中写盘或刷日志。</summary>
        /// <param name="value">零到一的音量。</param>
        public void SetMasterVolume(float value) { AudioManager.Instance.GlobalVolume = value; PlayerPrefs.SetFloat(MasterKey, MasterVolume); }
        /// <summary>实时更新音乐音量。</summary>
        /// <param name="value">零到一的音量。</param>
        public void SetMusicVolume(float value) { AudioManager.Instance.BGVolume = value; PlayerPrefs.SetFloat(MusicKey, MusicVolume); }
        /// <summary>实时更新音效音量。</summary>
        /// <param name="value">零到一的音量。</param>
        public void SetEffectVolume(float value) { AudioManager.Instance.EffectVolume = value; PlayerPrefs.SetFloat(EffectKey, EffectVolume); }
        /// <summary>在菜单关闭或架构注销时统一持久保存偏好。</summary>
        public void SavePreferences() { PlayerPrefs.Save(); WSLog.Log("[GameSettingsManager] 全局音量偏好已保存。"); }
        #endregion
    }
}
