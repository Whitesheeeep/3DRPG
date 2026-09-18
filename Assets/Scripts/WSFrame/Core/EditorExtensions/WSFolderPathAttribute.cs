using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// 标记字符串字段为 Unity 项目 Assets 文件夹路径，编辑器中会显示文件夹选择按钮。
    /// 字段最终保存为以 Assets 开头的项目相对路径，而不是操作系统绝对路径。
    /// </summary>
    public sealed class WSFolderPathAttribute : PropertyAttribute
    {
    }
}
