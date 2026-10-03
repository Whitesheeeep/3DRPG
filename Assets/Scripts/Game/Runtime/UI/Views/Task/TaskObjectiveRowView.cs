using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Task
{
    /// <summary>呈现任务详情中的一个目标进度行。</summary>
    [DisallowMultipleComponent]
    [InfoBox("挂在 TaskObjectiveRow Prefab 根节点；TMP_Text 与状态 Image 引用必须指向该行的子物体，并配置完成与未完成 Sprite。")]
    public sealed class TaskObjectiveRowView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private TMP_Text descriptionText;
        [SerializeField, Required] private Image statusMark;
        [SerializeField, Required] private Sprite pendingMarkSprite;
        [SerializeField, Required] private Sprite completedMarkSprite;
        private Color defaultTextColor;
        private float defaultFontSize;
        #endregion

        #region 属性
        /// <summary>获取行文本，供窗口组件复用其字体样式。</summary>
        public TMP_Text DescriptionText => descriptionText;
        #endregion

        #region 生命周期
        /// <summary>检查独立目标行 Prefab 的序列化子物体引用并缓存默认文字样式。</summary>
        /// <exception cref="InvalidOperationException">文本或状态标记缺失时抛出。</exception>
        private void Awake()
        {
            ValidateConfiguration();
            statusMark.raycastTarget = false;
            descriptionText.raycastTarget = false;
            defaultTextColor = descriptionText.color;
            defaultFontSize = descriptionText.fontSize;
        }

        /// <summary>校验独立目标行 Prefab 的文本、标记和状态 Sprite 引用。</summary>
        /// <exception cref="InvalidOperationException">Prefab 缺少必要引用时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (descriptionText == null || statusMark == null || pendingMarkSprite == null || completedMarkSprite == null)
                throw new InvalidOperationException("[TaskObjectiveRowView] 目标行 Prefab 缺少文本、状态 Image 或状态 Sprite。");
        }
        #endregion

        #region 行展示
        /// <summary>显示一个目标的说明、当前进度、需求值和完成状态。</summary>
        /// <param name="description">面向玩家的目标说明。</param>
        /// <param name="current">当前累计值。</param>
        /// <param name="required">完成目标所需数量。</param>
        /// <param name="isComplete">目标是否已完成。</param>
        public void BindObjective(string description, int current, int required, bool isComplete)
        {
            gameObject.SetActive(true);
            statusMark.gameObject.SetActive(true);
            statusMark.sprite = isComplete ? completedMarkSprite : pendingMarkSprite;
            descriptionText.color = isComplete
                ? new Color(0.66f, 0.66f, 0.66f, 1f)
                : defaultTextColor;
            descriptionText.fontSize = isComplete ? defaultFontSize - 1f : defaultFontSize;
            statusMark.color = Color.white;
            descriptionText.text = $"{description}  {current}/{required}";
        }

        /// <summary>以单独的强调行显示领奖业务拒绝原因。</summary>
        /// <param name="message">可重试的用户提示。</param>
        public void BindMessage(string message)
        {
            gameObject.SetActive(true);
            statusMark.gameObject.SetActive(false);
            descriptionText.color = new Color(0.93f, 0.62f, 0.43f, 1f);
            descriptionText.fontSize = defaultFontSize;
            descriptionText.text = message;
        }

        /// <summary>隐藏当前不属于所选阶段的复用目标行。</summary>
        public void Hide() => gameObject.SetActive(false);
        #endregion
    }
}
