using System;
using System.Collections.Generic;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.Views.WeaponDevelopment;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules;

namespace WS_Modules.UIModule
{
    /// <summary>武器培养窗口的序列化绑定容器。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 TemplateWindow 根结构：UIMask 与 UIContent 必须是直接子节点；页面 View 和选择面板由 Inspector 显式绑定。")]
    public sealed class WeaponDevelopmentWindowDataComponent : MonoBehaviour
    {
        #region 配置字段

        [SerializeField] private bool isFullWindow;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private WeaponDevelopmentView view;
        [SerializeField] private ItemSelectionPanelView selectionPanel;
        [SerializeField] private Button closeButton;
        [SerializeField, WSAddressableKey("UISpriteAtlas")] private List<string> dynamicAtlasAddresses = new() { "WeaponIcons" };
        [SerializeField, MinValue(0f)] private float atlasReleaseDelaySeconds = 30f;

        #endregion

        #region 属性

        /// <summary>获取窗口是否参与全屏伪隐藏。</summary>
        public bool IsFullWindow => isFullWindow;
        /// <summary>获取是否启用标准窗口动画。</summary>
        public bool DoAnimation => doAnimation;
        /// <summary>获取页面表现 View。</summary>
        public WeaponDevelopmentView View => view;
        /// <summary>获取通用物品选择面板。</summary>
        public ItemSelectionPanelView SelectionPanel => selectionPanel;
        /// <summary>获取关闭按钮。</summary>
        public Button CloseButton => closeButton;
        /// <summary>获取动态图集地址。</summary>
        public IReadOnlyList<string> DynamicAtlasAddresses => dynamicAtlasAddresses;
        /// <summary>获取动态图集延迟释放时间。</summary>
        public float AtlasReleaseDelaySeconds => atlasReleaseDelaySeconds;

        #endregion

        #region 校验

        /// <summary>校验培养窗口的显式序列化绑定。</summary>
        public void ValidateConfiguration()
        {
            if (view == null) throw new InvalidOperationException("[WeaponDevelopmentWindowDataComponent] 未绑定 WeaponDevelopmentView。");
            if (dynamicAtlasAddresses == null) throw new InvalidOperationException("[WeaponDevelopmentWindowDataComponent] 动态图集地址列表为空。");
            view.ValidateConfiguration();
            if (selectionPanel == null) throw new InvalidOperationException("[WeaponDevelopmentWindowDataComponent] 未绑定 ItemSelectionPanelView。");
            selectionPanel.ValidateConfiguration();
            if (closeButton == null) throw new InvalidOperationException("[WeaponDevelopmentWindowDataComponent] 未绑定关闭按钮。");
        }

        #endregion
    }
}
