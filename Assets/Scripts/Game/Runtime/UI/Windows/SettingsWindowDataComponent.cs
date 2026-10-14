using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI
{
    /// <summary>保存设定窗口的显式控件引用，不持有全局偏好或存档状态。</summary>
    public sealed class SettingsWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段
        /// <summary>当前档位文本。</summary>
        [Required] public TMP_Text CurrentSlotText;
        /// <summary>最后保存时间。</summary>
        [Required] public TMP_Text LastSaveText;
        /// <summary>保存及失败反馈。</summary>
        [Required] public TMP_Text StatusText;
        /// <summary>四个队伍头像。</summary>
        [Required] public Image[] Portraits = new Image[4];
        /// <summary>手动保存按钮。</summary>
        [Required] public Button SaveButton;
        /// <summary>保存并返回主界面按钮。</summary>
        [Required] public Button ReturnTitleButton;
        /// <summary>右上关闭按钮。</summary>
        [Required] public Button CloseButton;
        /// <summary>返回游戏按钮。</summary>
        [Required] public Button ContinueButton;
        /// <summary>主音量滑条。</summary>
        [Required] public Slider MasterSlider;
        /// <summary>音乐滑条。</summary>
        [Required] public Slider MusicSlider;
        /// <summary>音效滑条。</summary>
        [Required] public Slider EffectSlider;
        /// <summary>主音量百分比。</summary>
        [Required] public TMP_Text MasterValueText;
        /// <summary>音乐百分比。</summary>
        [Required] public TMP_Text MusicValueText;
        /// <summary>音效百分比。</summary>
        [Required] public TMP_Text EffectValueText;
        #endregion
    }
}
