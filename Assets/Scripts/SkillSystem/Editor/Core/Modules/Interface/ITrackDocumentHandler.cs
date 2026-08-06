#if UNITY_EDITOR
using System;
using UnityEditor;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 描述一种轨道在 SerializedObject 中的结构、帧规则和类型化内容编辑能力。
    /// </summary>
    internal interface ITrackDocumentHandler
    {
        /// <summary>该处理器对应的唯一 TrackConfig 具体类型。</summary>
        Type TrackType { get; }
        string ItemsPropertyName { get; }
        string StartFramePropertyName { get; }
        string DurationPropertyName { get; }
        string DefaultTrackNamePrefix { get; }
        bool SupportsResize { get; }

        /// <summary>
        /// 初始化一个新内容项的公共帧字段与类型专用字段。
        /// </summary>
        /// <param name="item">新建内容对应的 SerializedProperty。</param>
        /// <param name="id">分配给新 Clip 或 Marker 的稳定 Item GUID。</param>
        /// <param name="startFrame">新内容所在的非负整数帧。</param>
        void InitializeItem(UnityEditor.SerializedProperty item, string id, int startFrame);

        /// <summary>
        /// 通过 Document 事务创建一批类型化内容项。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request);

        /// <summary>
        /// 通过 Document 事务编辑一个类型化内容项。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        EditResult EditItem(Document document, string trackId, string itemId, IItemEditRequest request);

        /// <summary>
        /// 复制全部类型专用字段；实现必须保证 SerializeReference 等可变数据不会共享实例。
        /// </summary>
        /// <param name="source">保持不变的权威源 Item。</param>
        /// <param name="destination">已经初始化公共 GUID 与帧字段的目标 Item。</param>
        void CopySpecificFields(UnityEditor.SerializedProperty source,
            UnityEditor.SerializedProperty destination);

        /// <summary>
        /// 在 FPS 改变时重采样该类型除起止帧之外的帧字段。
        /// </summary>
        /// <param name="item">正在重采样的 Item。</param>
        /// <param name="oldFrameRate">修改前 FPS。</param>
        /// <param name="newFrameRate">修改后 FPS。</param>
        void ResampleSpecificFrameFields(UnityEditor.SerializedProperty item,
            int oldFrameRate, int newFrameRate);
    }
}
#endif
