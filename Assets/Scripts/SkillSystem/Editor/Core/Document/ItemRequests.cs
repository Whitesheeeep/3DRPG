#if UNITY_EDITOR
namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 标识可由轨道模块处理的内容批量创建请求。
    /// </summary>
    internal interface IItemCreateRequest
    {
    }

    /// <summary>
    /// 标识可由轨道模块处理的内容字段编辑请求。
    /// </summary>
    internal interface IItemEditRequest
    {
    }
}
#endif