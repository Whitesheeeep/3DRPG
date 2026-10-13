using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>只负责显示 HUD 顶部 Boss 等级与 ASC Health 比例的静态视图。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUDWindow Prefab 中显式绑定的 Boss 血条根节点、填充 Image 与等级 TMP_Text。")]
    public sealed class HUDBossHealthView : MonoBehaviour
    {
        #region 依赖字段

        // 这些引用由 HUDWindow Prefab 固定配置，运行时不搜索层级或创建控件。
        [SerializeField, Required] private GameObject bossHealthRootObject;
        [SerializeField, Required] private Image healthFillImage;
        [SerializeField, Required] private TMP_Text levelText;

        #endregion

        #region 配置校验

        /// <summary>确认 HUD 静态资源已显式绑定所有 Boss Health 控件。</summary>
        public void ValidateConfiguration()
        {
            if (bossHealthRootObject == null || healthFillImage == null || levelText == null)
                throw new System.InvalidOperationException(
                    "[HUDBossHealthView] HUDWindow Prefab 未绑定 Boss 血条根节点、填充 Image 或等级文本。");
        }

        #endregion

        #region 显示与刷新

        /// <summary>显示 Boss 血条并应用当前等级与 Health 快照。</summary>
        /// <param name="level">NPCController 的实际生成等级。</param>
        /// <param name="health">ASC 当前 Health。</param>
        /// <param name="maxHealth">ASC 当前 MaxHealth。</param>
        public void Show(int level, float health, float maxHealth)
        {
            bossHealthRootObject.SetActive(true);
            levelText.text = $"Lv.{level}";
            RefreshHealth(health, maxHealth);
        }

        /// <summary>按 ASC 当前 Health 与 MaxHealth 刷新填充比例。</summary>
        /// <param name="health">ASC 当前 Health。</param>
        /// <param name="maxHealth">ASC 当前 MaxHealth。</param>
        public void RefreshHealth(float health, float maxHealth)
        {
            healthFillImage.fillAmount = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
        }

        /// <summary>隐藏 Boss 血条并清除上次显示的等级和填充值。</summary>
        public void Clear()
        {
            levelText.text = string.Empty;
            healthFillImage.fillAmount = 0f;
            bossHealthRootObject.SetActive(false);
        }

        #endregion
    }
}
