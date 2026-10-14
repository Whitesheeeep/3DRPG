using System;
using RPG.Game.Loading;
using RPG.Game.UI.Views.Save;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.UI
{
    /// <summary>保存开始游戏窗口的显式控件引用和目标场景配置。</summary>
    [DisallowMultipleComponent]
    [InfoBox("必须绑定标题、开始按钮、状态文字、十档列表、选档提示、删除确认控件及庭院 SceneLoadConfig；所有引用对应 Prefab 中显式配置的 UIContent 节点。")]
    public sealed class GameStartWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("标题")]
        private TextMeshProUGUI titleText;
        [SerializeField, Required, LabelText("开始按钮")]
        private Button startButton;
        [SerializeField, Required, LabelText("开始按钮文字")]
        private TextMeshProUGUI startButtonText;
        [SerializeField, Required, LabelText("状态或错误提示")]
        private TextMeshProUGUI statusText;
        [SerializeField, Required, LabelText("目标场景配置")]
        private SceneLoadConfig targetSceneConfig;

        [SerializeField, Required] private SaveSlotListView slotList;
        [SerializeField, Required] private TMP_Text selectionText;
        [SerializeField, Required] private GameObject deletePanel;
        [SerializeField, Required] private TMP_Text deletePrompt;
        [SerializeField, Required] private Button confirmDeleteButton;
        [SerializeField, Required] private Button cancelDeleteButton;

        #endregion

        #region 窗口行为

        /// <summary>获取窗口是否按全屏遮挡处理。</summary>
        public bool IsFullWindow => true;
        /// <summary>获取标题文字控件。</summary>
        public TextMeshProUGUI TitleText => titleText;
        /// <summary>获取开始窗口是否启用 WindowBase 标准动画。</summary>
        public bool DoAnimation => false;
        /// <summary>获取开始按钮。</summary>
        public Button StartButton => startButton;
        /// <summary>获取按钮文字组件。</summary>
        public TextMeshProUGUI StartButtonText => startButtonText;
        /// <summary>获取错误提示组件。</summary>
        public TextMeshProUGUI StatusText => statusText;
        /// <summary>获取开始游戏请求使用的场景配置。</summary>
        public SceneLoadConfig TargetSceneConfig => targetSceneConfig;
        /// <summary>十档横向列表。</summary>
        public SaveSlotListView SlotList => slotList;
        /// <summary>当前选档说明。</summary>
        public TMP_Text SelectionText => selectionText;
        /// <summary>删除确认区域。</summary>
        public GameObject DeletePanel => deletePanel;
        /// <summary>删除卡位提示。</summary>
        public TMP_Text DeletePrompt => deletePrompt;
        /// <summary>确认删除按钮。</summary>
        public Button ConfirmDeleteButton => confirmDeleteButton;
        /// <summary>取消删除按钮。</summary>
        public Button CancelDeleteButton => cancelDeleteButton;

        #endregion

        #region 配置校验

        /// <summary>验证 Prefab 上所有窗口控件和场景配置均已显式绑定。</summary>
        /// <exception cref="InvalidOperationException">任一必需引用缺失时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (titleText == null || startButton == null || startButtonText == null ||
                statusText == null || targetSceneConfig == null || slotList == null || selectionText == null ||
                deletePanel == null || deletePrompt == null || confirmDeleteButton == null || cancelDeleteButton == null)
            {
                WSLog.LogError($"[GameStartWindowDataComponent] '{name}' 缺少窗口控件或目标 SceneLoadConfig 引用。");
                throw new InvalidOperationException($"[GameStartWindowDataComponent] '{name}' 缺少窗口控件或目标 SceneLoadConfig 引用。");
            }
        }

        #endregion
    }
}
