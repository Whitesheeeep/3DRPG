using System;
using TMPro;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>ChoiceWindow 与 DialogueWindow 共用的选项行，统一处理选择、点击和可用状态。</summary>
    [InfoBox("依赖同一 GameObject 上的 Button，以及自身或子节点中的 TMP_Text；缺失时必须先补齐这两个 UI 组件。")]
    public sealed class OptionChoice : MonoBehaviour, ISelectHandler,
        IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler
    {
        #region 组件引用与状态

        // 行 View 依赖 Button 和 TMP 文本组件完成显示与点击转发；Awake 会从同对象和子节点解析缺失引用。
        [SerializeField] private Button optionButton;
        [SerializeField] private TMP_Text optionText;
        // 仅当鼠标在当前行内实际移动超过该屏幕像素距离时，才允许鼠标重新取得 EventSystem Selection。
        [SerializeField, Min(0f)] private float pointerSelectionDistance = 8f;

        private Action<int> clickHandler;
        private Action<int> selectionHandler;
        private Action<int> pointerSelectionHandler;
        private int optionIndex;
        private Vector2 pointerAnchor;
        private int trackingPointerId = int.MinValue;
        private bool pointerTracking;

        #endregion

        #region 生命周期

        /// <summary>解析并缓存 Button 与 TMP 文本依赖。</summary>
        private void Awake()
        {
            if (optionButton == null) optionButton = GetComponent<Button>();
            if (optionText == null) optionText = GetComponentInChildren<TMP_Text>();
        }

        /// <summary>销毁前移除本行注册的点击监听。</summary>
        private void OnDestroy()
        {
            if (optionButton != null) optionButton.onClick.RemoveListener(HandleButtonClicked);
            clickHandler = null;
            selectionHandler = null;
            pointerSelectionHandler = null;
            pointerTracking = false;
            trackingPointerId = int.MinValue;
        }

        #endregion

        #region 绑定与刷新

        /// <summary>绑定窗口级点击、EventSystem 选中和鼠标移动选中回调；监听在行复用期间保持不变。</summary>
        /// <param name="handler">接收行索引的点击回调。</param>
        /// <param name="selectionChanged">接收 EventSystem 选中行索引的回调。</param>
        /// <param name="pointerSelectionRequested">接收鼠标达到移动阈值后的行索引回调。</param>
        public void Initialize(Action<int> handler, Action<int> selectionChanged,
            Action<int> pointerSelectionRequested)
        {
            if (optionButton == null || optionText == null)
                throw new InvalidOperationException("OptionChoice 必须绑定 Button 和 TMP_Text。");

            optionButton.onClick.RemoveListener(HandleButtonClicked);
            clickHandler = handler;
            selectionHandler = selectionChanged;
            pointerSelectionHandler = pointerSelectionRequested;
            optionButton.onClick.AddListener(HandleButtonClicked);
        }

        /// <summary>绑定点击和 EventSystem 选中回调；不启用鼠标移动选中。</summary>
        /// <param name="handler">接收行索引的点击回调。</param>
        /// <param name="selectionChanged">接收 EventSystem 选中行索引的回调。</param>
        public void Initialize(Action<int> handler, Action<int> selectionChanged)
            => Initialize(handler, selectionChanged, null);

        /// <summary>绑定窗口级点击回调；保留无选中回调重载兼容其他选项 View。</summary>
        /// <param name="handler">接收行索引的点击回调。</param>
        public void Initialize(Action<int> handler)
            => Initialize(handler, null, null);

        /// <summary>设置当前行索引、显示文本和可交互状态。</summary>
        /// <param name="index">当前行在 ChoiceWindow 列表中的索引。</param>
        /// <param name="text">待显示的选项名称。</param>
        /// <param name="interactable">按钮是否允许点击和 EventSystem Submit。</param>
        public void SetOption(int index, string text, bool interactable)
        {
            optionIndex = index;
            optionText.text = text ?? string.Empty;
            optionButton.interactable = interactable;
            // 行可能在鼠标仍停留时被复用；下一次 Move 重新建立锚点，避免沿用旧选项位置。
            pointerTracking = false;
            trackingPointerId = int.MinValue;
        }

        /// <summary>获取该行实际使用的 Unity Button。</summary>
        public Button Button => optionButton;

        /// <summary>清除当前行文本并禁用按钮；视觉状态由 EventSystem 重新计算。</summary>
        public void ClearOption()
        {
            if (optionText != null) optionText.text = string.Empty;
            if (optionButton != null) optionButton.interactable = false;
            pointerTracking = false;
            trackingPointerId = int.MinValue;
        }

        #endregion

        #region 点击事件

        /// <summary>响应 Button 点击并把当前行索引交给 ChoiceWindow。</summary>
        private void HandleButtonClicked() => clickHandler?.Invoke(optionIndex);

        /// <summary>响应 Unity EventSystem 的选中结果，并通知所属 View 同步领域 Selection。</summary>
        /// <param name="eventData">Unity 传入的选中事件数据。</param>
        public void OnSelect(BaseEventData eventData) => selectionHandler?.Invoke(optionIndex);

        /// <summary>鼠标进入当前行时建立新的屏幕坐标锚点，不立即抢走键盘或手柄 Selection。</summary>
        /// <param name="eventData">Unity 传入的指针进入事件数据。</param>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsMousePointer(eventData) || optionButton == null || !optionButton.IsInteractable())
            {
                pointerTracking = false;
                trackingPointerId = int.MinValue;
                return;
            }

            pointerTracking = true;
            trackingPointerId = eventData.pointerId;
            pointerAnchor = eventData.position;
        }

        /// <summary>
        /// 在当前行内累计鼠标的净屏幕位移；达到阈值后请求 View 安全切换 EventSystem Selection。
        /// </summary>
        /// <param name="eventData">Unity 传入的指针移动事件数据。</param>
        public void OnPointerMove(PointerEventData eventData)
        {
            if (!IsMousePointer(eventData) || optionButton == null || !optionButton.IsInteractable()) return;

            if (!pointerTracking || trackingPointerId != eventData.pointerId)
            {
                // 某些输入模块可能先派发 Move 再补发 Enter；第一次 Move 只建立锚点，避免刚进入就抢焦点。
                pointerTracking = true;
                trackingPointerId = eventData.pointerId;
                pointerAnchor = eventData.position;
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
            {
                // 当前行已被鼠标或键盘选中时跟随鼠标位置，键盘移开后仍能从最近位置重新计算阈值。
                pointerAnchor = eventData.position;
                return;
            }

            float threshold = Mathf.Max(0f, pointerSelectionDistance);
            Vector2 displacement = eventData.position - pointerAnchor;
            if (displacement.sqrMagnitude < threshold * threshold) return;

            // 先更新锚点再通知 View，保证 View 的同步 Selection 回调不会重复使用旧位移。
            pointerAnchor = eventData.position;
            pointerSelectionHandler?.Invoke(optionIndex);
        }

        /// <summary>鼠标离开当前行时清除锚点，下一次进入必须重新移动完整阈值。</summary>
        /// <param name="eventData">Unity 传入的指针离开事件数据。</param>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!pointerTracking || eventData.pointerId != trackingPointerId) return;
            pointerTracking = false;
            trackingPointerId = int.MinValue;
        }

        /// <summary>仅接受 Input System 的真实鼠标指针事件，避免笔、触摸或 XR 指针改变键盘 Selection。</summary>
        /// <param name="eventData">待判断的指针事件。</param>
        /// <returns>是否为鼠标/笔指针。</returns>
        private static bool IsMousePointer(PointerEventData eventData)
        {
            ExtendedPointerEventData extended = eventData as ExtendedPointerEventData;
            return extended != null && extended.pointerType == UIPointerType.MouseOrPen &&
                (extended.device == null || extended.device is Mouse);
        }

        #endregion
    }
}
