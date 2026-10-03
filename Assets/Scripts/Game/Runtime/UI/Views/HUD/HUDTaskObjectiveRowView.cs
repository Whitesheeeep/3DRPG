using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>显示追踪任务当前阶段中的一个目标及其完成进度。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUDTaskObjectiveRow Prefab 中显式绑定的状态 Image、目标 TMP 文本、空心菱形 Sprite 和绿色勾 Sprite；行由 HUDTaskTrackerView 按需创建并复用。")]
    public sealed class HUDTaskObjectiveRowView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private Image statusMarkImage;
        [SerializeField, Required] private TMP_Text objectiveText;
        [SerializeField, Required] private Sprite pendingMarkSprite;
        [SerializeField, Required] private Sprite completedMarkSprite;
        private Color defaultTextColor;
        private float defaultFontSize;
        #endregion

        #region 初始化与配置校验
        /// <summary>在 Prefab 默认样式仍未被目标状态覆盖时缓存文字基准样式。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            statusMarkImage.raycastTarget = false;
            objectiveText.raycastTarget = false;
            defaultTextColor = objectiveText.color;
            defaultFontSize = objectiveText.fontSize;
        }

        /// <summary>校验行视图在 HUD Prefab 中的序列化绑定。</summary>
        /// <exception cref="System.InvalidOperationException">Prefab 缺少必要 UI 引用时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (statusMarkImage == null || objectiveText == null || pendingMarkSprite == null || completedMarkSprite == null)
                throw new System.InvalidOperationException("[HUDTaskObjectiveRowView] 目标行未绑定状态 Image、目标文本或状态 Sprite。");

        }
        #endregion

        #region 行展示
        /// <summary>更新一条目标的文案、数值、图像标记和文字完成样式。</summary>
        /// <param name="description">回退处理后的玩家目标说明。</param>
        /// <param name="current">当前进度。</param>
        /// <param name="required">完成所需进度。</param>
        /// <param name="isComplete">目标是否已完成。</param>
        /// <exception cref="System.ArgumentException">目标说明为空时抛出。</exception>
        public void Bind(string description, int current, int required, bool isComplete)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new System.ArgumentException("HUD 目标说明不能为空。", nameof(description));

            statusMarkImage.gameObject.SetActive(true);
            statusMarkImage.sprite = isComplete ? completedMarkSprite : pendingMarkSprite;
            statusMarkImage.color = Color.white;
            objectiveText.text = $"{description}  {current}/{required}";
            objectiveText.color = isComplete
                ? new Color(0.66f, 0.66f, 0.66f, 1f)
                : defaultTextColor;
            objectiveText.fontSize = isComplete ? defaultFontSize - 1f : defaultFontSize;
            gameObject.SetActive(true);
        }
        #endregion
    }
}
