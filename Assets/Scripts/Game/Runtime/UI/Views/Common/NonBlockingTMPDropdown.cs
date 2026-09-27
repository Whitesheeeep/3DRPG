using TMPro;
using UnityEngine;

namespace RPG.Game.UI.Views.Common
{
    /// <summary>
    /// 展开选项时不创建覆盖整个画布的透明拦截层。
    /// </summary>
    public sealed class NonBlockingTMPDropdown : TMP_Dropdown
    {
        /// <summary>
        /// 返回空值以保留候选列表本身的高层 Canvas，同时让画布其他区域继续接收输入。
        /// </summary>
        /// <param name="rootCanvas">TMP 下拉框所属的根画布。</param>
        /// <returns>始终返回空值，表示不创建全屏 Blocker。</returns>
        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            Debug.Log("[NonBlockingTMPDropdown] 展开选项，不创建全屏 Blocker。", this);
            return null;
        }
    }
}
