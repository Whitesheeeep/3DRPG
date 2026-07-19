using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using WS_Modules.Extensions;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    public abstract class WindowBase : WindowBehaviour
    {
        // 可选遮罩节点，用于阻挡点击事件穿透；窗口允许没有 UIMask
        private CanvasGroup _UIMaskCanvasGroup;
        // 用于控制UI交互的CanvasGroup组件，作为窗口本体的交互控制，可以通过调整 alpha 和 interactable 来实现淡入淡出和交互控制
        private CanvasGroup _CanvasGroup;
        // UI内容的父物体，所有UI元素都应该作为这个物体的子物体，以便于统一管理和控制
        private Transform _UIContent;

        private List<Toggle> _ToggleList = new List<Toggle>(); //所有的Toggle列表
        private List<Button> _AllButtonList = new List<Button>(); //所有Button列表
        private List<InputField> _InputList = new List<InputField>(); //所有的输入框列表

        // 留个接口，方便外部调用来禁用动画，适用于一些特殊场景，比如：循环弹出时，第一次弹出需要动画，后续的弹出就不需要动画了
        protected bool _disableAnim = false; //禁用动画

        public virtual void OnAwake(GameObject gameObject, Transform transform, Canvas canvas, string name,
            Camera camera)
        {
            this.GameObject = gameObject;
            this.Transform = transform;
            this.Canvas = canvas;
            this.Name = name;
            if (this.Canvas != null)
            {
                this.Canvas.worldCamera = camera;
            }
            else
            {
                WSLog.LogWarning($"{Name} 缺少 Canvas 组件，窗口排序、遮罩动画和渲染层级可能无法正常工作");
            }
            
            OnAwake();
        }

        public virtual void OnAwake(GameObject gameObject, Camera camera)
        {
            OnAwake(gameObject, gameObject.transform, 
                gameObject.GetComponent<Canvas>(), gameObject.name, camera);
        }

        public override void OnAwake()
        {
            base.OnAwake();
            InitializeBaseComponent();
        }

        public override void OnShow()
        {
            base.OnShow();
            WSLog.Log($"{Name} OnShow");
            ShowAnimation();
        }

        public override void OnHide()
        {
            base.OnHide();
            WSLog.Log($"{Name} OnHide");
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            RemoveAllButtonListener();
            RemoveAllInputListener();
            RemoveAllToggleListener();
            _AllButtonList.Clear();
            _InputList.Clear();
            _ToggleList.Clear();
            WSLog.Log($"{Name} OnDestroy");
        }

        /// <summary>
        /// 初始化基类组件
        /// </summary>
        private void InitializeBaseComponent()
        {
            _CanvasGroup = Transform.GetOrAddComponent<CanvasGroup>();
            _UIMaskCanvasGroup = Transform.Find("UIMask")?.GetOrAddComponent<CanvasGroup>();
            _UIContent = Transform.Find("UIContent")?.transform;

            if (_UIContent == null)
            {
                WSLog.LogWarning($"{Name} 缺少 UIContent 节点，弹窗缩放动画将被跳过");
            }
        }

        #region 动画管理
        /// <summary>
        /// 如果不需要动画请设置 doAnimation 为 false 来禁用动画，适用于一些特殊场景，比如：循环弹出时，第一次弹出需要动画，后续的弹出就不需要动画了
        /// </summary>
        /// <param name="doAnimation"></param>
        protected virtual void SetDoAnimation(bool doAnimation) => _disableAnim = !doAnimation;

        public void ShowAnimation()
        {
            //基础弹窗不需要动画
            if (Canvas != null && Canvas.sortingOrder > 90 && !_disableAnim)
            {
                // WSLog.Log("Play Show Animation! " + Name);
                //Mask动画
                if (_UIMaskCanvasGroup != null)
                {
                    _UIMaskCanvasGroup.alpha = 0;
                    DOTween.To(() => _UIMaskCanvasGroup.alpha, value => _UIMaskCanvasGroup.alpha = value, 1f, 0.2f);
                }

                //缩放动画
                if (_UIContent != null)
                {
                    _UIContent.localScale = Vector3.one * 0.8f;
                    _UIContent.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
                }
            }
        }

        public void HideAnimation()
        {
            if (Canvas != null && Canvas.sortingOrder > 90 && !_disableAnim && _UIContent != null)
            {
                _UIContent.DOScale(Vector3.one * 1.1f, 0.2f).SetEase(Ease.OutBack).OnComplete(() =>
                {
                    UIManager.Instance.HideWindow(Name);
                });
            }
            else
            {
                UIManager.Instance.HideWindow(Name);
            }
        }
        #endregion

        public void HideWindow()
        {
            HideAnimation();
        }

        
        // 伪隐藏：窗口虽然在视觉上是隐藏的，但实际上它仍然存在于场景中，并且可以继续接收事件和交互，
        // 这种方式适用于一些特殊场景，比如：需要在隐藏窗口的同时保持窗口的状态和数据不变，或者需要在隐藏窗口的同时继续监听一些事件
        // 通过调整 CanvasGroup 的属性来实现伪隐藏，可以将 alpha 设置为 0 来使窗口不可见，同时保持 interactable 和 blocksRaycasts 的值为 true，这样窗口虽然看不见了，但仍然可以接收点击事件和交互。
        public void PseudoHidden(bool canInteract)
        {
            if (_CanvasGroup != null)
            {
                _CanvasGroup.alpha = canInteract ? 1 : 0;
                _CanvasGroup.interactable = canInteract;
                _CanvasGroup.blocksRaycasts = canInteract;
            }

            if (_UIMaskCanvasGroup != null)
            {
                _UIMaskCanvasGroup.alpha = canInteract ? 1 : 0;
                _UIMaskCanvasGroup.interactable = canInteract;
                _UIMaskCanvasGroup.blocksRaycasts = canInteract;
            }
        }

        /// <summary>
        /// 通过调整 CanvasGroup 的属性来控制窗口的显示和隐藏，这样可以实现淡入淡出和交互控制，
        /// 而不是直接通过 SetActive 来控制，这样可以避免一些性能问题和状态管理问题，同时也可以实现一些特殊的显示效果，
        /// 比如淡入淡出等
        /// 简而言之：代替 SetActive
        /// </summary>
        /// <param name="isVisble"></param>
        public override void SetVisible(bool isVisble)
        {
            if (_CanvasGroup == null)
            {
                WSLog.LogError("CanvasGroup is Null!" + Name);
                return;
            }

            Visible = isVisble;
            _CanvasGroup.alpha = isVisble ? 1 : 0;
            _CanvasGroup.interactable = isVisble;
            _CanvasGroup.blocksRaycasts = isVisble;
            // 如果窗口是可见的，并且需要在显示时进行同层级重绘渲染，那么先将窗口设置为不可见再设置为可见，
            // 这样可以触发 Unity 的渲染机制，重新渲染窗口，
            // 从而解决一些特殊情况下的渲染问题，比如窗口被其他 UI 遮挡或者窗口的某些元素没有正确渲染等问题
            if (isVisble && PopStack)
            {
                GameObject.SetActive(false);
                GameObject.SetActive(true);
            }
        }

        #region 事件管理
        public void AddButtonClickListener(Button btn, UnityAction action)
        {
            if (btn != null)
            {
                if (!_AllButtonList.Contains(btn))
                {
                    _AllButtonList.Add(btn);
                }

                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(action);
            }
        }

        // 这里将 Toggle 自身回传给回调函数，
        // 方便在多个 Toggle 共用同一个回调方法时（如 ToggleGroup），
        // 通过 toggle 参数区分具体是哪个 Toggle 触发了事件。
        // 区分多个来源（最常见场景） 通常在一个界面中（例如设置面板的画质选择：低、中、高），会有多个 Toggle 属于同一个 Group。如果不传 Toggle 本身，回调函数只收得到 true/false，无法区分是用户点了“低”还是“高”。
        // 传了 Toggle 引用后，你就可以这样写
        /*void OnQualityChange(bool isOn, Toggle toggle)
                  {
                      if (!isOn) return; // 只处理被选中的那个

                      if (toggle == lowQualityToggle) { /* 设置低画质 #1# }
                      else if (toggle == highQualityToggle) { /* 设置高画质 #1# }

                      // 或者直接读名字
                      Debug.Log("用户选择了：" + toggle.name);
                  }
        */
        public void AddToggleClickListener(Toggle toggle, UnityAction<bool, Toggle> action)
        {
            if (toggle != null)
            {
                if (!_ToggleList.Contains(toggle))
                {
                    _ToggleList.Add(toggle);
                }

                toggle.onValueChanged.RemoveAllListeners();
                toggle.onValueChanged.AddListener((isOn) => { action?.Invoke(isOn, toggle); });
            }
        }

        public void AddInputFieldListener(InputField input, UnityAction<string> onChangeAction,
            UnityAction<string> endAction)
        {
            if (input != null)
            {
                if (!_InputList.Contains(input))
                {
                    _InputList.Add(input);
                }

                input.onValueChanged.RemoveAllListeners();
                input.onEndEdit.RemoveAllListeners();
                input.onValueChanged.AddListener(onChangeAction);
                input.onEndEdit.AddListener(endAction);
            }
        }

        public void RemoveAllButtonListener()
        {
            foreach (var item in _AllButtonList)
            {
                item.onClick.RemoveAllListeners();
            }
        }

        public void RemoveAllToggleListener()
        {
            foreach (var item in _ToggleList)
            {
                item.onValueChanged.RemoveAllListeners();
            }
        }

        public void RemoveAllInputListener()
        {
            foreach (var item in _InputList)
            {
                item.onValueChanged.RemoveAllListeners();
                item.onEndEdit.RemoveAllListeners();
            }
        }
        #endregion

        public void SetMaskVisible(bool isVisible)
        {
            // WSLog.Log("SetMaskVisible: " + isVisible);
            if (_UIMaskCanvasGroup != null)
            {
                _UIMaskCanvasGroup.alpha = isVisible ? 1 : 0;
                _UIMaskCanvasGroup.interactable = isVisible;
                _UIMaskCanvasGroup.blocksRaycasts = isVisible;
                if (isVisible && PopStack)
                {
                    _UIMaskCanvasGroup.gameObject.SetActive(false);
                    _UIMaskCanvasGroup.gameObject.SetActive(true);
                }
            }
        }
    }
}

