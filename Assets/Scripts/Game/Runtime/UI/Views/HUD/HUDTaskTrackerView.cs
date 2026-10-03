using System;
using System.Collections.Generic;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>展示当前追踪任务标题、阶段目标、可领奖状态和可用距离。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUDWindow Prefab 中显式绑定的标题、分类图标、状态行和目标容器，以及独立 HUDTaskObjectiveRow Prefab；面板与目标位置由 UGUI LayoutGroup 计算，本视图仅显示事实，不确认任务未读。")]
    public sealed class HUDTaskTrackerView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private Image categoryIconImage;
        [SerializeField, Required] private TMP_Text titleText;
        [SerializeField, Required] private RectTransform claimableRectTransform;
        [SerializeField, Required] private TMP_Text claimableText;
        [SerializeField, Required] private RectTransform distanceRectTransform;
        [SerializeField, Required] private TMP_Text distanceText;
        [SerializeField, Required] private RectTransform objectiveRowsRectTransform;
        [SerializeField, Required] private HUDTaskObjectiveRowView objectiveRowPrefab;
        [SerializeField, Required] private Sprite mainTaskSprite;
        [SerializeField, Required] private Sprite sideTaskSprite;
        private readonly List<HUDTaskObjectiveRowView> objectiveRowViewList =
            new List<HUDTaskObjectiveRowView>();
        private string currentDistanceLabel = string.Empty;
        private int objectiveCount;
        #endregion

        #region 配置与查询
        /// <summary>校验固定 HUD 控件和独立目标行 Prefab 的引用，并初始化摘要显隐。</summary>
        /// <exception cref="InvalidOperationException">Prefab 缺少必要引用时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (categoryIconImage == null || titleText == null ||
                claimableRectTransform == null || claimableText == null || distanceRectTransform == null ||
                distanceText == null || objectiveRowsRectTransform == null || objectiveRowPrefab == null ||
                mainTaskSprite == null || sideTaskSprite == null)
            {
                throw new InvalidOperationException("[HUDTaskTrackerView] HUD 任务摘要 Prefab 存在未绑定引用。");
            }

            objectiveRowPrefab.ValidateConfiguration();
            claimableRectTransform.gameObject.SetActive(false);
            distanceRectTransform.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>绑定当前追踪任务的固定摘要字段并打开摘要面板。</summary>
        /// <param name="title">任务标题。</param>
        /// <param name="categoryId">固定任务分类 ID。</param>
        /// <param name="claimable">任务是否等待玩家领取奖励。</param>
        public void RenderHeader(string title, TaskCategoryId categoryId, bool claimable)
        {
            titleText.text = title;
            categoryIconImage.sprite = categoryId.Value == TaskCategoryCatalog.MainIdValue
                ? mainTaskSprite
                : sideTaskSprite;
            claimableRectTransform.gameObject.SetActive(claimable);
            claimableText.text = claimable ? "可领取奖励" : string.Empty;
            gameObject.SetActive(true);
        }

        /// <summary>设置当前阶段目标数量，并复用已有行或从独立 Prefab 补足行池。</summary>
        /// <param name="count">当前阶段目标数。</param>
        /// <exception cref="ArgumentOutOfRangeException">目标数为负数时抛出。</exception>
        public void SetObjectiveCount(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "目标行数量不能为负数。");

            while (objectiveRowViewList.Count < count)
            {
                HUDTaskObjectiveRowView rowView = Instantiate(objectiveRowPrefab, objectiveRowsRectTransform);
                rowView.name = $"ObjectiveRow_{objectiveRowViewList.Count + 1}";
                rowView.gameObject.SetActive(false);
                objectiveRowViewList.Add(rowView);
                Debug.Log($"[HUDTaskTrackerView] 按需创建目标行，index={objectiveRowViewList.Count - 1}");
            }

            objectiveCount = count;
            objectiveRowsRectTransform.gameObject.SetActive(count > 0);
            for (int index = 0; index < objectiveRowViewList.Count; index++)
                objectiveRowViewList[index].gameObject.SetActive(index < count);

        }

        /// <summary>取得目标行池中指定索引的实例供 Controller 写入展示数据。</summary>
        /// <param name="index">目标行索引。</param>
        /// <returns>已创建的目标行 View。</returns>
        /// <exception cref="ArgumentOutOfRangeException">索引不在当前目标数量范围内时抛出。</exception>
        public HUDTaskObjectiveRowView GetObjectiveRowView(int index)
        {
            if (index < 0 || index >= objectiveCount)
                throw new ArgumentOutOfRangeException(nameof(index), "目标行索引不在当前阶段目标范围内。");

            return objectiveRowViewList[index];
        }

        /// <summary>更新可选距离行；空字符串会隐藏该行。</summary>
        /// <param name="distanceLabel">格式化距离，如 12m；没有目标位置时为空。</param>
        public void SetDistanceLabel(string distanceLabel)
        {
            distanceLabel ??= string.Empty;
            if (string.Equals(currentDistanceLabel, distanceLabel, StringComparison.Ordinal))
                return;

            currentDistanceLabel = distanceLabel;
            bool hasDistance = !string.IsNullOrWhiteSpace(distanceLabel);
            distanceRectTransform.gameObject.SetActive(hasDistance);
            distanceText.text = distanceLabel;
        }

        /// <summary>隐藏任务摘要而保留行池，以便下次追踪时复用对象。</summary>
        public void Hide()
        {
            SetDistanceLabel(string.Empty);
            gameObject.SetActive(false);
        }
        #endregion
    }
}
