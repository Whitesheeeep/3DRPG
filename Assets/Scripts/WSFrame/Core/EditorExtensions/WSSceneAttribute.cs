using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// 标记 string 或 int 字段为可加载场景选择字段。
    /// string 字段保存场景名，int 字段保存 Build Settings 中的 buildIndex。
    /// </summary>
    public sealed class WSSceneAttribute : PropertyAttribute
    {
    }
}
