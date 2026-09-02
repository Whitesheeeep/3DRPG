using System;
using UnityEngine;

namespace RPG.CurrencySystem
{
    /// <summary>货币配置和成长消耗使用的稳定字符串标识。</summary>
    [Serializable]
    public enum CurrencyId
    {
        [InspectorName("无货币")]
        None = 0,
        [InspectorName("摩拉")]
        Mola = 1,
        [InspectorName("原石")]
        YuanShi = 2,
    }
}
