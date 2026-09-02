using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>圣遗物的五个固定部位。</summary>
    public enum ArtifactSlot
    {
        /// <summary>生之花。</summary>
        [InspectorName("生之花")] FlowerOfLife = 0,
        /// <summary>死之羽。</summary>
        [InspectorName("死之羽")] PlumeOfDeath = 1,
        /// <summary>时之沙。</summary>
        [InspectorName("时之沙")] SandsOfEon = 2,
        /// <summary>空之杯。</summary>
        [InspectorName("空之杯")] GobletOfEonothem = 3,
        /// <summary>理之冠。</summary>
        [InspectorName("理之冠")] CircletOfLogos = 4
    }
}
