using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using WS_Modules.Extensions;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口基类，承载窗口组件、交互监听、可见性和由管理器编排的过渡动画。
    /// </summary>
    public abstract class WindowBase : WindowBehaviour
    {
        #region 依赖与状态字段

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

        // 动画过渡由生命周期服务等待；窗口销毁时主动终止，避免异步流程悬挂。
        private Tween activeTransitionTween;
        private UniTaskCompletionSource transitionCompletionSource;

        #endregion

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

        /// <summary>
        /// 执行窗口显示生命周期回调；动画由生命周期服务在回调后统一驱动。
        /// </summary>
        public override void OnShow()
        {
            base.OnShow();
            WSLog.Log($"{Name} OnShow");
        }

        public override void OnHide()
        {
            base.OnHide();
            WSLog.Log($"{Name} OnHide");
        }

        /// <summary>
        /// 释放窗口监听和当前动画，并执行基类销毁回调。
        /// </summary>
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
            StopTransitionAnimation();
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
                WSLog.LogWarning($"{Name} 缺少 UIContent 节点，默认缩放动画将被跳过，但透明度动画仍可执行");
            }
        }

        /// <summary>
        /// 获取窗口根节点的 CanvasGroup，供派生窗口创建自定义动画使用。
        /// </summary>
        protected CanvasGroup WindowCanvasGroup => _CanvasGroup;

        /// <summary>
        /// 获取窗口内容节点，供派生窗口创建自定义动画使用。
        /// </summary>
        protected Transform UIContent => _UIContent;

        #region 动画管理
        /// <summary>
        /// 如果不需要动画请设置 doAnimation 为 false 来禁用动画，适用于一些特殊场景，比如：循环弹出时，第一次弹出需要动画，后续的弹出就不需要动画了
        /// </summary>
        /// <param name="doAnimation"></param>
        protected virtual void SetDoAnimation(bool doAnimation) => _disableAnim = !doAnimation;

        /// <summary>
        /// 创建窗口显示动画；派生窗口可以返回自定义 Tween 或 Sequence。
        /// </summary>
        /// <returns>显示动画；返回 null 表示无需动画。</returns>
        protected virtual Tween ShowAnimation()
        {
            if (Canvas == null || Canvas.sortingOrder <= 90)
            {
                return null;
            }

            Sequence sequence = DOTween.Sequence();
            bool hasAnimation = false;
            if (_CanvasGroup != null)
            {
                // 根 CanvasGroup 统一控制窗口内容和遮罩的渐入，避免对子节点重复叠加透明度动画。
                _CanvasGroup.alpha = 0;
                sequence.Join(DOTween.To(() => _CanvasGroup.alpha,
                    value => _CanvasGroup.alpha = value, 1f, 0.2f));
                hasAnimation = true;
            }

            if (_UIContent != null)
            {
                _UIContent.localScale = Vector3.one * 0.8f;
                sequence.Join(_UIContent.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack));
                hasAnimation = true;
            }

            if (!hasAnimation)
            {
                sequence.Kill(false);
                return null;
            }

            return sequence;
        }

        /// <summary>
        /// 创建窗口隐藏动画；派生窗口可以返回自定义 Tween 或 Sequence。
        /// </summary>
        /// <returns>隐藏动画；返回 null 表示无需动画。</returns>
        protected virtual Tween HideAnimation()
        {
            if (Canvas == null || Canvas.sortingOrder <= 90)
            {
                return null;
            }

            Sequence sequence = DOTween.Sequence();

            // hasAnimation 用于判断是否有动画需要播放，如果没有动画就直接返回一个已完成的 UniTask，避免不必要的等待。
            bool hasAnimation = false;
            if (_CanvasGroup != null)
            {
                // 隐藏时根节点渐出，遮罩随根节点一起消失，层级服务仍可单独控制遮罩归属。
                sequence.Join(DOTween.To(() => _CanvasGroup.alpha,
                    value => _CanvasGroup.alpha = value, 0f, 0.2f));
                hasAnimation = true;
            }

            if (_UIContent != null)
            {
                sequence.Join(_UIContent.DOScale(Vector3.one * 0.8f, 0.2f).SetEase(Ease.InQuad));
                hasAnimation = true;
            }

            if (!hasAnimation)
            {
                sequence.Kill(false);
                return null;
            }

            // 默认隐藏动画完成时窗口已经透明，此处复位只影响下一次打开的起始状态。
            sequence.OnComplete(() =>
            {
                if (_UIContent != null)
                {
                    _UIContent.localScale = Vector3.one;
                }
            });
            return sequence;
        }

        /// <summary>
        /// 播放显示过渡并返回可等待的完成任务；生命周期状态由生命周期服务负责切换。
        /// </summary>
        /// <returns>显示动画完成或被终止时完成的任务。</returns>
        internal UniTask PlayShowAnimationAsync()
        {
            StopTransitionAnimation();
            return _disableAnim ? UniTask.CompletedTask : AwaitTransition(ShowAnimation());
        }

        /// <summary>
        /// 播放隐藏过渡并返回可等待的完成任务；隐藏完成后由生命周期服务提交最终状态。
        /// </summary>
        /// <returns>隐藏动画完成或被终止时完成的任务。</returns>
        internal UniTask PlayHideAnimationAsync()
        {
            StopTransitionAnimation();
            return _disableAnim ? UniTask.CompletedTask : AwaitTransition(HideAnimation());
        }

        /// <summary>
        /// 终止当前窗口动画并完成等待者，用于 Shutdown 或销毁阶段的强制清理。
        /// </summary>
        internal void StopTransitionAnimation()
        {
            Tween tween = activeTransitionTween;
            UniTaskCompletionSource completionSource = transitionCompletionSource;
            activeTransitionTween = null;
            transitionCompletionSource = null;
            completionSource?.TrySetResult();
            tween?.Kill();
        }

        /// <summary>
        /// 将 DOTween 完成回调转换为 UniTask，并记录当前窗口的动画引用。
        /// </summary>
        /// <param name="tween">待等待的动画。</param>
        /// <returns>动画完成任务。</returns>
        private UniTask AwaitTransition(Tween tween)
        {
            if (tween == null)
            {
                return UniTask.CompletedTask;
            }

            // 暂停派生类刚创建的 Tween，再放入外层 Sequence，保证由基类统一启动和终止。
            tween.Pause();
            Tween managedTween = DOTween.Sequence().Join(tween);
            UniTaskCompletionSource completionSource = new UniTaskCompletionSource();
            activeTransitionTween = managedTween;
            transitionCompletionSource = completionSource;
            managedTween.OnComplete(() => CompleteTransition(managedTween, completionSource));
            managedTween.OnKill(() => CompleteTransition(managedTween, completionSource));
            return completionSource.Task;
        }

        /// <summary>
        /// 完成指定窗口动画等待；过期 Tween 的回调不会触碰新的过渡。
        /// </summary>
        /// <param name="completedTween">已完成或被终止的外层 Tween。</param>
        /// <param name="completionSource">该次过渡的完成源。</param>
        private void CompleteTransition(Tween completedTween, UniTaskCompletionSource completionSource)
        {
            bool isCurrentTransition = ReferenceEquals(activeTransitionTween, completedTween) &&
                                        ReferenceEquals(transitionCompletionSource, completionSource);
            if (isCurrentTransition)
            {
                // 先解除当前引用，再完成等待者；这样等待者立即启动的新动画不会被旧回调清理。
                activeTransitionTween = null;
                transitionCompletionSource = null;
            }

            // 即使这是已经被新动画取代的旧 Tween，也必须完成它自己的等待源，但不能改动新动画字段。
            completionSource.TrySetResult();
        }
        #endregion

        /// <summary>
        /// 请求管理器隐藏当前窗口；动画和生命周期由管理器统一驱动。
        /// </summary>
        public void HideWindow()
        {
            UIManager.Instance.HideWindow(Name);
        }

        
        // 伪隐藏：窗口仍保留在注册表和场景中，但通过 CanvasGroup 控制视觉和交互状态，不触发生命周期隐藏。
        // 这种方式适用于全屏窗口覆盖底层界面时保留其业务状态，并在上层关闭后恢复显示。
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

