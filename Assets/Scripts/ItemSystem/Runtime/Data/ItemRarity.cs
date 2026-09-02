using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>物品和武器的星级稀有度。</summary>
    public enum ItemRarity
    {
        /// <summary>一星。</summary>
        [InspectorName("一星")] One = 1,
        /// <summary>二星。</summary>
        [InspectorName("二星")] Two = 2,
        /// <summary>三星。</summary>
        [InspectorName("三星")] Three = 3,
        /// <summary>四星。</summary>
        [InspectorName("四星")] Four = 4,
        /// <summary>五星。</summary>
        [InspectorName("五星")] Five = 5
    }
}
