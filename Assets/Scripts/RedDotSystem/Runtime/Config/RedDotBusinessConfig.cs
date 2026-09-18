using UnityEngine;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 红点业务配置的共同标记基类。
    /// 业务配置只保存 Manager 写入红点节点所需的 Asset 引用，不持有运行时数值。
    /// </summary>
    public abstract class RedDotBusinessConfig : ScriptableObject
    {
    }
}
