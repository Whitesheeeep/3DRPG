using System;
using TMPro;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 将一个 RedDotKey 的聚合状态显示为 UGUI 红点图标，并按需显示数字。
    /// 组件宿主保持启用，只有独立的 VisualRoot 会随数值显隐。
    /// </summary>
    public sealed class RedDotUGUIBadge : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Tooltip("需要观察的红点节点 Asset。")]
        private RedDotKey key;

        [SerializeField, Tooltip("只负责视觉显隐的子节点，不得是挂载本组件的宿主。")]
        private GameObject visualRoot;

        [SerializeField, Tooltip("是否在红点图标上显示聚合数值。关闭时仅显示图标。")]
        private bool showValue;

        [SerializeField, Tooltip("可选的数字文本；为空或关闭 Show Value 时只显示图标。")]
        private TMP_Text valueText;

        #endregion

        #region 运行时状态

        private RedDotSystem redDotSystem;
        private IUnRegister valueChangedUnregister;

        #endregion

        #region 生命周期

        /// <summary>启用徽标时连接唯一 RedDotSystem 并立即渲染当前值。</summary>
        private void OnEnable()
        {
            ValidateReferences();
            redDotSystem = RPG.Game.GameArchitecture.Interface.GetSystem<RedDotSystem>();
            valueChangedUnregister = redDotSystem.RegisterValueChanged(key, OnValueChanged);
            int currentValue = redDotSystem.GetValue(key);
            RenderValue(currentValue);
            Debug.Log(
                $"[RedDotUGUIBadge] 已绑定红点徽标，key={key.DerivedPath}，value={currentValue}。",
                this);
        }

        /// <summary>禁用徽标时释放定向订阅，避免窗口重复启用产生多次回调。</summary>
        private void OnDisable()
        {
            valueChangedUnregister?.UnRegister();
            valueChangedUnregister = null;
            redDotSystem = null;
            Debug.Log(
                $"[RedDotUGUIBadge] 已解绑红点徽标，key={(key == null ? "<null>" : key.name)}。",
                this);
        }

        #endregion

        #region 事件与渲染

        /// <summary>响应目标节点的聚合数值变化。</summary>
        /// <param name="changedEvent">红点数值变化事件。</param>
        private void OnValueChanged(RedDotValueChangedEvent changedEvent)
        {
            RenderValue(changedEvent.CurrentValue);
        }

        /// <summary>
        /// 按红点聚合值更新图标显隐，并在显式开启且配置 TMP 时更新数字。
        /// </summary>
        /// <param name="value">节点聚合数值。</param>
        private void RenderValue(int value)
        {
            bool visible = value > 0;
            visualRoot.SetActive(visible);

            bool shouldShowValue = showValue && valueText != null;
            if (valueText == null)
            {
                return;
            }

            // 即使 Prefab 仍保留旧 TMP，也由 Show Value 统一控制，避免无数字设计残留占位文字。
            valueText.gameObject.SetActive(shouldShowValue && visible);
            if (!shouldShowValue || !visible)
            {
                valueText.text = string.Empty;
                return;
            }

            valueText.text = value > 99 ? "99+" : value.ToString();
        }

        #endregion

        #region 配置校验

        /// <summary>校验徽标的显式节点和视觉引用，避免运行时猜测组件或静默失效。</summary>
        /// <exception cref="InvalidOperationException">节点或视觉根缺失，或视觉根错误指向宿主时抛出。</exception>
        private void ValidateReferences()
        {
            if (key == null)
            {
                throw new InvalidOperationException(
                    $"[RedDotUGUIBadge] GameObject={name} 未绑定 RedDotKey。请在 Inspector 中显式配置。");
            }

            if (visualRoot == null)
            {
                throw new InvalidOperationException(
                    $"[RedDotUGUIBadge] GameObject={name} 未绑定 VisualRoot。请在 Inspector 中显式配置。");
            }

            if (ReferenceEquals(visualRoot, gameObject))
            {
                throw new InvalidOperationException(
                    $"[RedDotUGUIBadge] GameObject={name} 的 VisualRoot 不能是徽标组件宿主本身。");
            }
        }

        #endregion
    }
}
