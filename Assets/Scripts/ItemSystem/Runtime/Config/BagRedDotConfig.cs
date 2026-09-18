using RPG.RedDotSystemNS;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>
    /// 保存背包五类 New 红点叶节点的业务写入引用。
    /// 该资产不持有红点运行时状态，也不向 UI 提供显示节点映射。
    /// </summary>
    [CreateAssetMenu(
        fileName = "BagRedDotConfig",
        menuName = "RPG/ItemSystem/Bag Red Dot Config",
        order = 40)]
    public sealed class BagRedDotConfig : RedDotBusinessConfig
    {
        #region 配置字段

        [SerializeField, Tooltip("Weapon 分类的 New 红点叶节点。")]
        private RedDotKey weaponNewKey;

        [SerializeField, Tooltip("Artifact 分类的 New 红点叶节点。")]
        private RedDotKey artifactNewKey;

        [SerializeField, Tooltip("DevelopmentExperienceItem 分类的 New 红点叶节点。")]
        private RedDotKey developmentExperienceItemNewKey;

        [SerializeField, Tooltip("Food 分类的 New 红点叶节点。")]
        private RedDotKey foodNewKey;

        [SerializeField, Tooltip("DevelopmentItem 分类的 New 红点叶节点。")]
        private RedDotKey developmentItemNewKey;

        #endregion

        #region 公开查询

        /// <summary>获取武器 New 红点叶节点。</summary>
        public RedDotKey WeaponNewKey => weaponNewKey;

        /// <summary>获取圣遗物 New 红点叶节点。</summary>
        public RedDotKey ArtifactNewKey => artifactNewKey;

        /// <summary>获取养成经验道具 New 红点叶节点。</summary>
        public RedDotKey DevelopmentExperienceItemNewKey => developmentExperienceItemNewKey;

        /// <summary>获取食物 New 红点叶节点。</summary>
        public RedDotKey FoodNewKey => foodNewKey;

        /// <summary>获取养成道具 New 红点叶节点。</summary>
        public RedDotKey DevelopmentItemNewKey => developmentItemNewKey;

        #endregion
    }
}
