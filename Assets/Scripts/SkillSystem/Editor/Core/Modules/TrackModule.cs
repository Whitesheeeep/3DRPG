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
    /// 聚合一种轨道的投影、数据、拖入、Item View 与 Inspector 能力，但不持有窗口状态。
    /// </summary>
    internal sealed class TrackModule
    {
        public ITrackProjection Projection { get; }
        public ITrackDocumentHandler Document { get; }
        public ITrackDropHandler Drop { get; }
        public IItemViewFactory ItemFactory { get; }
        public IInspectorDrawer ItemInspector { get; }
        public ITrackPreviewFactory PreviewFactory { get; }

        /// <summary>
        /// 创建不可变轨道模块；Drop 和 PreviewFactory 可为空以声明未提供对应能力。
        /// </summary>
        public TrackModule(ITrackProjection projection, ITrackDocumentHandler document,
            ITrackDropHandler drop, IItemViewFactory itemFactory, IInspectorDrawer itemInspector,
            ITrackPreviewFactory previewFactory = null)
        {
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Drop = drop;
            ItemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
            ItemInspector = itemInspector ?? throw new ArgumentNullException(nameof(itemInspector));
            PreviewFactory = previewFactory;
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
                new AnimationItemFactory(), new AnimationInspectorDrawer(), new AnimationPreviewFactory()));
            registry.Register(new TrackModule(
                new AttackDetectionProjection(), new AttackDetectionDocumentHandler(), null,
                new AttackDetectionItemFactory(), new AttackDetectionInspectorDrawer()));
            registry.Register(new TrackModule(
                new VfxProjection(), new VfxDocumentHandler(), new VfxDropHandler(config),
                new VfxItemFactory(), new VfxInspectorDrawer(), new VfxPreviewFactory()));
            registry.Register(new TrackModule(
                new AudioProjection(), new AudioDocumentHandler(), new AudioDropHandler(),
                new AudioItemFactory(), new AudioInspectorDrawer(), new AudioPreviewFactory()));
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

        /// <summary>
        /// 按模块注册顺序创建当前窗口私有的轨道预览处理器。
        /// </summary>
        public IReadOnlyList<ITrackPreviewHandler> CreatePreviewHandlers() =>
            modules
                .Where(module => module.PreviewFactory != null)
                .Select(module => module.PreviewFactory.Create())
                .ToArray();
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