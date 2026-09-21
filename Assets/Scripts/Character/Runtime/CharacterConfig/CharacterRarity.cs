using UnityEngine;

namespace RPG.Character
{
    /// <summary>角色配置使用的一至五星稀有度。</summary>
    public enum CharacterRarity
    {
        /// <summary>一星角色。</summary>
        [InspectorName("一星")] One = 1,
        /// <summary>二星角色。</summary>
        [InspectorName("二星")] Two = 2,
        /// <summary>三星角色。</summary>
        [InspectorName("三星")] Three = 3,
        /// <summary>四星角色。</summary>
        [InspectorName("四星")] Four = 4,
        /// <summary>五星角色。</summary>
        [InspectorName("五星")] Five = 5
    }
}
