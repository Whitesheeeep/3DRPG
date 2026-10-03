using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 任务窗口的序列化依赖容器，集中保存页签、列表、详情和操作控件引用。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 TaskWindow 根节点下显式绑定的分类按钮、列表分组、详情文本、奖励区和 TaskItem Prefab。")]
    public sealed class TaskWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段

        // WindowBase 根据该配置决定窗口遮挡策略和显示过渡；列表模板供后续数据接入复用。
        [SerializeField] private bool isFullWindow = true;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private Button closeButton;
        [SerializeField, Required] private Button[] categoryButtons = Array.Empty<Button>();
        [SerializeField, Required] private GameObject[] categorySelectionBackgrounds = Array.Empty<GameObject>();
        [SerializeField, Required] private GameObject[] categorySelectionUnderlines = Array.Empty<GameObject>();
        [SerializeField, Required] private GameObject mainQuestSectionRoot;
        [SerializeField, Required] private GameObject sideQuestSectionRoot;
        [SerializeField, Required] private TMP_Text emptyStateText;
        [SerializeField, Required] private GameObject taskDetailsPanel;
        [SerializeField, Required] private TMP_Text taskTitleText;
        [SerializeField, Required] private TMP_Text stageAndObjectivesText;
        [SerializeField, Required] private TMP_Text taskDescriptionText;
        [SerializeField, Required] private GameObject rewardSection;
        [SerializeField, Required] private TMP_Text rewardsText;
        [SerializeField, Required] private Button trackingButton;
        [SerializeField, Required] private TMP_Text trackingButtonLabel;
        [SerializeField, Required] private GameObject locationSection;
        [SerializeField, Required] private GameObject timeRemainingPanel;
        [SerializeField, Required] private GameObject taskItemPrefab;

        #endregion

        #region 属性

        /// <summary>获取窗口是否参与全屏窗口层级行为。</summary>
        public bool IsFullWindow => isFullWindow;

        /// <summary>获取窗口是否播放 WindowBase 标准过渡动画。</summary>
        public bool DoAnimation => doAnimation;

        /// <summary>获取显式绑定的关闭按钮。</summary>
        public Button CloseButton => closeButton;

        /// <summary>获取按主线、支线顺序排列的分类按钮。</summary>
        public IReadOnlyList<Button> CategoryButtons => categoryButtons;

        /// <summary>获取按分类顺序绑定的选中背景。</summary>
        public IReadOnlyList<GameObject> CategorySelectionBackgrounds => categorySelectionBackgrounds;

        /// <summary>获取按分类顺序绑定的选中横条。</summary>
        public IReadOnlyList<GameObject> CategorySelectionUnderlines => categorySelectionUnderlines;

        /// <summary>获取主线分组根节点和动态列表父节点。</summary>
        public GameObject MainQuestSectionRoot => mainQuestSectionRoot;

        /// <summary>获取支线分组根节点和动态列表父节点。</summary>
        public GameObject SideQuestSectionRoot => sideQuestSectionRoot;

        /// <summary>获取无任务时显示的空态文本。</summary>
        public TMP_Text EmptyStateText => emptyStateText;

        /// <summary>获取右侧详情面板根节点。</summary>
        public GameObject TaskDetailsPanel => taskDetailsPanel;

        /// <summary>获取任务标题文本。</summary>
        public TMP_Text TaskTitleText => taskTitleText;

        /// <summary>获取阶段标题和目标文本。</summary>
        public TMP_Text StageAndObjectivesText => stageAndObjectivesText;

        /// <summary>获取任务说明文本。</summary>
        public TMP_Text TaskDescriptionText => taskDescriptionText;

        /// <summary>获取奖励区域根节点。</summary>
        public GameObject RewardSection => rewardSection;

        /// <summary>获取奖励展示文本。</summary>
        public TMP_Text RewardsText => rewardsText;

        /// <summary>获取开始或取消追踪按钮。</summary>
        public Button TrackingButton => trackingButton;

        /// <summary>获取追踪按钮文案。</summary>
        public TMP_Text TrackingButtonLabel => trackingButtonLabel;

        /// <summary>获取本次暂不展示的地点区域。</summary>
        public GameObject LocationSection => locationSection;

        /// <summary>获取本次暂不展示的剩余时间区域。</summary>
        public GameObject TimeRemainingPanel => timeRemainingPanel;

        /// <summary>获取可复用的任务条目 Prefab。</summary>
        public GameObject TaskItemPrefab => taskItemPrefab;

        #endregion

        #region 配置校验

        /// <summary>校验任务窗口必需的序列化引用和主、支线页签顺序。</summary>
        /// <exception cref="InvalidOperationException">必需的 UI 对象未在 Prefab 中绑定时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (closeButton == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定关闭按钮。");
            if (categoryButtons == null || categoryButtons.Length != 2)
                throw new InvalidOperationException("[TaskWindowDataComponent] 必须按主线、支线顺序绑定两个分类按钮。");
            if (categorySelectionBackgrounds == null || categorySelectionBackgrounds.Length != 2 ||
                categorySelectionUnderlines == null || categorySelectionUnderlines.Length != 2)
                throw new InvalidOperationException("[TaskWindowDataComponent] 分类选中背景和横条必须各绑定两个。");
            for (int index = 0; index < 2; index++)
            {
                if (categoryButtons[index] == null || categorySelectionBackgrounds[index] == null ||
                    categorySelectionUnderlines[index] == null)
                    throw new InvalidOperationException($"[TaskWindowDataComponent] 分类页签索引 {index} 的引用不完整。");
            }
            if (mainQuestSectionRoot == null || sideQuestSectionRoot == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定主线或支线任务分组。");
            if (emptyStateText == null || taskDetailsPanel == null || taskTitleText == null ||
                stageAndObjectivesText == null || taskDescriptionText == null || rewardSection == null ||
                rewardsText == null || trackingButton == null || trackingButtonLabel == null ||
                locationSection == null || timeRemainingPanel == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 任务详情、奖励、追踪或隐藏区域引用不完整。");
            if (taskItemPrefab == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定 TaskItem Prefab。");
        }

        #endregion
    }
}
