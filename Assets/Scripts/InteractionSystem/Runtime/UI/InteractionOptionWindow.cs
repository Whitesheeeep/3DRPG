using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.UIModule;

namespace RPG.InteractionSystem.UI
{
    /// <summary>
    /// 显示当前交互 Option 列表和选中状态的常驻 HUD 窗口。
    /// </summary>
    public partial class InteractionOptionWindow : WindowBase
    {
        #region 视觉状态

        private readonly List<GameObject> rows = new();
        private RectTransform listRoot;

        #endregion

        #region 生命周期

        /// <summary>绑定生成组件并创建运行时列表容器。</summary>
        public override void OnAwake()
        {
            BindGeneratedComponents();
            base.OnAwake();
            CreateListRoot();
        }

        /// <summary>窗口显示时保持当前 UI 状态，具体列表由 Controller 首次刷新。</summary>
        public override void OnShow() => base.OnShow();

        /// <summary>清理运行时创建的 Option 行。</summary>
        public override void OnDestroy()
        {
            for (int index = 0; index < rows.Count; index++)
                if (rows[index] != null) Object.Destroy(rows[index]);
            rows.Clear();
            base.OnDestroy();
        }

        #endregion

        #region 刷新

        /// <summary>刷新 Option 文本、图标和选中高亮。</summary>
        /// <param name="options">当前最终 Option 列表。</param>
        /// <param name="selectedOption">当前选中 Option。</param>
        public void Refresh(IReadOnlyList<InteractionOption> options, InteractionOption selectedOption)
        {
            if (listRoot == null) return;
            ClearRows();

            bool hasOptions = options != null && options.Count > 0;
            for (int index = 0; hasOptions && index < options.Count; index++)
                CreateRow(options[index], selectedOption != null && options[index].Id == selectedOption.Id);

            // 使用 WindowBase 的伪隐藏保持窗口生命周期常驻，零 Option 时不拦截场景交互。
            PseudoHidden(hasOptions);
        }

        /// <summary>创建底部居中的列表根节点。</summary>
        private void CreateListRoot()
        {
            Transform content = Transform.Find("UIContent");
            if (content == null) return;

            GameObject rootObject = new GameObject("InteractionOptionList", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rootObject.transform.SetParent(content, false);
            listRoot = rootObject.GetComponent<RectTransform>();
            listRoot.anchorMin = new Vector2(0.5f, 0f);
            listRoot.anchorMax = new Vector2(0.5f, 0f);
            listRoot.pivot = new Vector2(0.5f, 0f);
            listRoot.anchoredPosition = new Vector2(0f, 32f);
            listRoot.sizeDelta = new Vector2(480f, 0f);

            VerticalLayoutGroup layout = rootObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            ContentSizeFitter fitter = rootObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>创建一行 Option 的背景、图标和文本。</summary>
        /// <param name="option">待显示 Option。</param>
        /// <param name="selected">该行是否是当前选中项。</param>
        private void CreateRow(InteractionOption option, bool selected)
        {
            GameObject rowObject = new GameObject("InteractionOption", typeof(RectTransform),
                typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(listRoot, false);
            rows.Add(rowObject);

            Image background = rowObject.GetComponent<Image>();
            background.color = selected
                ? new Color(0.15f, 0.55f, 1f, 0.9f)
                : new Color(0f, 0f, 0f, 0.68f);

            LayoutElement element = rowObject.GetComponent<LayoutElement>();
            element.minHeight = 34f;
            element.preferredHeight = 34f;

            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 5, 5);
            layout.spacing = 8f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            if (option.Icon != null)
            {
                GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                iconObject.transform.SetParent(rowObject.transform, false);
                Image icon = iconObject.GetComponent<Image>();
                icon.sprite = option.Icon;
                icon.preserveAspect = true;
                LayoutElement iconElement = iconObject.GetComponent<LayoutElement>();
                iconElement.preferredWidth = 24f;
                iconElement.preferredHeight = 24f;
            }

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            labelObject.transform.SetParent(rowObject.transform, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = option.DisplayName;
            label.fontSize = 18f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement labelElement = labelObject.GetComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;
        }

        /// <summary>销毁当前运行时创建的 Option 行。</summary>
        private void ClearRows()
        {
            for (int index = 0; index < rows.Count; index++)
                if (rows[index] != null) Object.Destroy(rows[index]);
            rows.Clear();
        }

        #endregion
    }
}
