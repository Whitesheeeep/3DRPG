#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.SkillSystem;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 负责把一种运行时轨道配置投影为 ViewData，并创建可跨刷新恢复的具体选择状态 SelectionState。
    /// </summary>
    internal interface ITrackProjection
    {
        Type GroupType { get; }
        Type TrackType { get; }
        Type ItemType { get; }
        Type GroupSelectionType { get; }
        Type TrackSelectionType { get; }
        Type ItemSelectionType { get; }

        /// <summary>
        /// 从技能配置创建该模块的完整分组投影。
        /// </summary>
        GroupViewData CreateGroup(SkillConfig config);
        /// <summary>
        /// 创建该模块的分组选择状态。
        /// </summary>
        SelectionState CreateGroupSelection();
        /// <summary>
        /// 使用稳定轨道 GUID 创建轨道选择状态。
        /// </summary>
        SelectionState CreateTrackSelection(string trackId);
        /// <summary>
        /// 使用稳定轨道与内容 GUID 创建内容选择状态。
        /// </summary>
        SelectionState CreateItemSelection(string trackId, string itemId);
        /// <summary>
        /// 使用新内容 GUID 克隆同类型内容选择。
        /// </summary>
        SelectionState CloneItemSelection(SelectionState selection, string itemId);
        /// <summary>
        /// 在该模块分组中查找选择对应的显示投影。
        /// </summary>
        IViewData FindSelection(GroupViewData group, SelectionState selection);
    }

    /// <summary>
    /// 描述一种轨道在 SerializedObject 中的结构、帧规则和类型化内容编辑能力。
    /// </summary>
    internal interface ITrackDocumentHandler
    {
        string TracksPropertyName { get; }
        string ItemsPropertyName { get; }
        string StartFramePropertyName { get; }
        string DurationPropertyName { get; }
        string DefaultTrackNamePrefix { get; }
        bool SupportsResize { get; }
        bool RequiresExclusiveIntervals { get; }

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
        /// 在数组复制后修复类型专用深拷贝字段，避免 SerializeReference 数据共享。
        /// </summary>
        /// <param name="source">复制后的权威源 Item。</param>
        /// <param name="destination">需要修复类型专用字段的新 Item。</param>
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

    /// <summary>
    /// 把一批 Project 素材校验并转换为稳定的内容创建请求，不直接修改资产。
    /// </summary>
    internal interface ITrackDropHandler
    {
        /// <summary>
        /// 判断整批 Project 素材是否可以被该轨道接收。
        /// </summary>
        bool CanAccept(IReadOnlyList<UnityEngine.Object> assets);

        /// <summary>
        /// 把已校验素材复制为稳定的类型化创建请求。
        /// </summary>
        /// <param name="assets">已校验的素材列表。</param>
        /// <param name="startFrame">新内容所在的非负整数帧。</param>
        /// <returns>创建的类型化创建请求。</returns>
        IItemCreateRequest CreateRequest(IReadOnlyList<UnityEngine.Object> assets, int startFrame);
    }

    /// <summary>
    /// 为一种具体 Item ViewData 创建对应的 UI Toolkit 小型视图。
    /// </summary>
    internal interface IItemViewFactory
    {
        /// <summary>
        /// 使用模块模板创建具体 Item View。
        /// </summary>
        ItemView Create(TrackViewData track, ItemViewData item, ElementFactory elements, CoordinateMapper mapper);
    }

    /// <summary>
    /// 聚合一种轨道的投影、数据、拖入、Item View 与 Inspector 能力，但不持有窗口状态。
    /// </summary>
    internal sealed class TrackModule
    {
        public ITrackProjection Projection { get; }
        public ITrackDocumentHandler Document { get; }
        public ITrackDropHandler Drop { get; }
        public IItemViewFactory ItemFactory { get; }
        public IInspectorDrawer ItemInspector { get; }

        /// <summary>
        /// 创建不可变轨道模块；Drop 可为空以声明该轨道不接收 Project 素材。
        /// </summary>
        public TrackModule(ITrackProjection projection, ITrackDocumentHandler document,
            ITrackDropHandler drop, IItemViewFactory itemFactory, IInspectorDrawer itemInspector)
        {
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Drop = drop;
            ItemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
            ItemInspector = itemInspector ?? throw new ArgumentNullException(nameof(itemInspector));
        }
    }

    /// <summary>
    /// 按具体投影与选择类型索引全部轨道模块，是窗口内唯一的轨道能力注册表。
    /// </summary>
    internal sealed class TrackModuleRegistry
    {
        #region 字段与属性

        private readonly List<TrackModule> modules = new();
        private readonly Dictionary<Type, TrackModule> groupModules = new();
        private readonly Dictionary<Type, TrackModule> trackModules = new();
        private readonly Dictionary<Type, TrackModule> itemModules = new();
        private readonly Dictionary<Type, TrackModule> selectionModules = new();
        private readonly IInspectorDrawer groupInspector = new GroupInspectorDrawer();
        private readonly IInspectorDrawer trackInspector = new TrackInspectorDrawer();

        public IReadOnlyList<TrackModule> Modules => modules;
        public IReadOnlyList<ITrackDocumentHandler> DocumentHandlers =>
            modules.Select(module => module.Document).ToArray();

        #endregion

        #region 创建与注册

        /// <summary>
        /// 创建按 Animation、AttackDetection、VFX、Audio、Event 排列的内置模块注册表。
        /// </summary>
        public static TrackModuleRegistry CreateDefault(EditorConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            TrackModuleRegistry registry = new();
            registry.Register(new TrackModule(
                new AnimationProjection(), new AnimationDocumentHandler(), new AnimationDropHandler(),
                new AnimationItemFactory(), new AnimationInspectorDrawer()));
            registry.Register(new TrackModule(
                new AttackDetectionProjection(), new AttackDetectionDocumentHandler(), null,
                new AttackDetectionItemFactory(), new AttackDetectionInspectorDrawer()));
            registry.Register(new TrackModule(
                new VfxProjection(), new VfxDocumentHandler(), new VfxDropHandler(config),
                new VfxItemFactory(), new VfxInspectorDrawer()));
            registry.Register(new TrackModule(
                new AudioProjection(), new AudioDocumentHandler(), new AudioDropHandler(),
                new AudioItemFactory(), new AudioInspectorDrawer()));
            registry.Register(new TrackModule(
                new EventProjection(), new EventDocumentHandler(), null,
                new EventItemFactory(), new EventInspectorDrawer()));
            return registry;
        }

        /// <summary>
        /// 注册一个轨道模块；任何投影或选择类型重复时立即失败。
        /// </summary>
        public void Register(TrackModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            ITrackProjection projection = module.Projection;
            RegisterType(groupModules, projection.GroupType, module, "Group ViewData");
            RegisterType(trackModules, projection.TrackType, module, "Track ViewData");
            RegisterType(itemModules, projection.ItemType, module, "Item ViewData");
            RegisterType(selectionModules, projection.GroupSelectionType, module, "Group Selection");
            RegisterType(selectionModules, projection.TrackSelectionType, module, "Track Selection");
            RegisterType(selectionModules, projection.ItemSelectionType, module, "Item Selection");
            modules.Add(module);
        }

        #endregion

        #region 查询

        /// <summary>
        /// 获取分组投影所属模块。
        /// </summary>
        public TrackModule Get(GroupViewData group) => GetRequired(groupModules, group?.GetType(), "分组");

        /// <summary>
        /// 获取轨道投影所属模块。
        /// </summary>
        public TrackModule Get(TrackViewData track) => GetRequired(trackModules, track?.GetType(), "轨道");

        /// <summary>
        /// 获取内容投影所属模块。
        /// </summary>
        public TrackModule Get(ItemViewData item) => GetRequired(itemModules, item?.GetType(), "内容");

        /// <summary>
        /// 获取具体选择所属模块。
        /// </summary>
        public TrackModule Get(SelectionState selection) =>
            GetRequired(selectionModules, selection?.GetType(), "选择");

        /// <summary>
        /// 尝试获取轨道的素材拖入能力；未注册 Drop 的轨道返回 false。
        /// </summary>
        public bool TryGetDrop(TrackViewData track, out ITrackDropHandler drop)
        {
            drop = null;
            if (track == null || !trackModules.TryGetValue(track.GetType(), out TrackModule module)) return false;
            drop = module.Drop;
            return drop != null;
        }

        /// <summary>
        /// 根据选中 ViewData 返回通用或模块专用 Inspector Drawer。
        /// </summary>
        public IInspectorDrawer GetInspector(IViewData viewData)
        {
            if (viewData is GroupViewData) return groupInspector;
            if (viewData is TrackViewData) return trackInspector;
            return viewData is ItemViewData item ? Get(item).ItemInspector : null;
        }

        /// <summary>
        /// 通过 Item 所属模块创建具体时间轴元素视图。
        /// </summary>
        public ItemView CreateItemView(TrackViewData track, ItemViewData item,
            ElementFactory elements, CoordinateMapper mapper) =>
            Get(item).ItemFactory.Create(track, item, elements, mapper);

        #endregion

        #region 内部校验

        // 把一种具体类型写入索引，并在重复注册时提供明确错误。
        private static void RegisterType(Dictionary<Type, TrackModule> index, Type type,
            TrackModule module, string role)
        {
            if (type == null) throw new InvalidOperationException($"轨道模块没有声明 {role} 类型。");
            if (!index.TryAdd(type, module))
                throw new InvalidOperationException($"{role} 类型 {type.FullName} 已被其他轨道模块注册。");
        }

        // 从精确类型索引获取模块，禁止未知类型静默回退到错误轨道。
        private static TrackModule GetRequired(Dictionary<Type, TrackModule> index, Type type, string role)
        {
            if (type != null && index.TryGetValue(type, out TrackModule module)) return module;
            throw new InvalidOperationException($"未注册{role}类型：{type?.FullName ?? "<null>"}");
        }

        #endregion
    }
}
#endif
