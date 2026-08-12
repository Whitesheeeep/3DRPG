#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.SkillSystem;
using UnityEditor;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 聚合一种 TrackConfig 的 Document、拖入、Item View、Inspector 与 Preview 能力。
    /// </summary>
    internal sealed class TrackModule
    {
        public Type TrackType { get; }
        public Type ItemType { get; }
        public TimelineTrackAttribute Metadata { get; }
        public ITrackDocumentHandler Document { get; }
        public ITrackDropHandler Drop { get; }
        public IItemViewFactory ItemFactory { get; }
        public IInspectorDrawer ItemInspector { get; }
        public ITrackPreviewFactory PreviewFactory { get; }

        /// <summary>
        /// 创建不可变轨道模块；Drop 与 PreviewFactory 可为空。
        /// </summary>
        public TrackModule(Type trackType, Type itemType, TimelineTrackAttribute metadata,
            ITrackDocumentHandler document, ITrackDropHandler drop,
            IItemViewFactory itemFactory, IInspectorDrawer itemInspector,
            ITrackPreviewFactory previewFactory = null)
        {
            TrackType = trackType ?? throw new ArgumentNullException(nameof(trackType));
            ItemType = itemType ?? throw new ArgumentNullException(nameof(itemType));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Drop = drop;
            ItemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
            ItemInspector = itemInspector ?? throw new ArgumentNullException(nameof(itemInspector));
            PreviewFactory = previewFactory;
            if (Document.TrackType != TrackType)
                throw new ArgumentException("Document Handler 的 TrackType 必须与 Module TrackType 一致。", nameof(document));
        }
    }

    /// <summary>
    /// 声明一种 TrackConfig 对应的完整编辑器能力组合。
    /// </summary>
    internal abstract class TrackModuleDefinition
    {
        public abstract Type TrackType { get; }
        public abstract Type ItemType { get; }

        /// <summary>
        /// 根据轨道元数据创建无状态模块。
        /// </summary>
        public abstract TrackModule Create(EditorConfig config, TimelineTrackAttribute metadata);
    }

    /// <summary>
    /// 使用 TypeCache 建立 TrackConfig 与 ModuleDefinition 的一对一注册表。
    /// </summary>
    internal sealed class TrackModuleRegistry
    {
        #region 字段与属性
        private readonly List<TrackModule> modules = new();
        private readonly Dictionary<Type, TrackModule> trackModules = new();
        private readonly Dictionary<Type, TrackModule> itemModules = new();
        private readonly IInspectorDrawer trackInspector = new TrackInspectorDrawer();
        public IReadOnlyList<TrackModule> Modules => modules;
        public IReadOnlyList<ITrackDocumentHandler> DocumentHandlers =>
            modules.Select(module => module.Document).ToArray();
        #endregion

        #region 创建与注册
        /// <summary>
        /// 扫描并按 TimelineTrack 顺序创建默认注册表。
        /// </summary>
        public static TrackModuleRegistry CreateDefault(EditorConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Dictionary<Type, TrackModuleDefinition> definitions = TypeCache
                .GetTypesDerivedFrom<TrackModuleDefinition>()
                .Where(type => !type.IsAbstract)
                .Select(type => (TrackModuleDefinition)Activator.CreateInstance(type))
                .ToDictionary(definition => definition.TrackType);
            Type[] trackTypes = TypeCache.GetTypesDerivedFrom<TrackConfigBase>()
                .Where(type => !type.IsAbstract && GetMetadataOrNull(type) != null)
                .OrderBy(type => GetMetadataOrNull(type).Order)
                .ThenBy(type => GetMetadataOrNull(type).MenuPath, StringComparer.Ordinal)
                .ToArray();

            TrackModuleRegistry registry = new();
            foreach (Type trackType in trackTypes)
            {
                if (!definitions.TryGetValue(trackType, out TrackModuleDefinition definition))
                    throw new InvalidOperationException($"轨道 {trackType.FullName} 缺少 TrackModuleDefinition。");
                registry.Register(definition.Create(config, GetMetadataOrNull(trackType)));
            }
            foreach (Type unused in definitions.Keys.Except(trackTypes))
                throw new InvalidOperationException($"ModuleDefinition 指向未注册轨道：{unused.FullName}");
            return registry;
        }

        /// <summary>
        /// 注册模块；重复的 Track 或 Item 类型立即失败。
        /// </summary>
        public void Register(TrackModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            RegisterType(trackModules, module.TrackType, module, "TrackConfig");
            RegisterType(itemModules, module.ItemType, module, "ItemConfig");
            modules.Add(module);
        }
        #endregion

        #region 查询
        /// <summary>
        /// 获取实际轨道子资产所属模块。
        /// </summary>
        public TrackModule Get(TrackConfigBase track) =>
            GetRequired(trackModules, track?.GetType(), "轨道");

        /// <summary>
        /// 获取实际内容配置所属模块。
        /// </summary>
        public TrackModule Get(TimelineItemConfigBase item) =>
            GetRequired(itemModules, item?.GetType(), "内容");

        /// <summary>
        /// 按 TrackConfig 类型获取模块。
        /// </summary>
        public TrackModule Get(Type trackType) => GetRequired(trackModules, trackType, "轨道");

        /// <summary>
        /// 尝试取得素材拖入能力。
        /// </summary>
        public bool TryGetDrop(TrackConfigBase track, out ITrackDropHandler drop)
        {
            drop = Get(track).Drop;
            return drop != null;
        }

        /// <summary>
        /// 返回 Track 公共 Drawer 或 Item 专用 Drawer。
        /// </summary>
        public IInspectorDrawer GetInspectorDrawer(object data)
        {
            if (data is TrackConfigBase) return trackInspector;
            return data is TimelineItemConfigBase item ? Get(item).ItemInspector : null;
        }

        /// <summary>
        /// 通过实际配置创建 Item View。
        /// </summary>
        public ItemView CreateItemView(TrackConfigBase track, TimelineItemConfigBase item,
            ElementFactory elements, CoordinateMapper mapper) =>
            Get(item).ItemFactory.Create(track, item, elements, mapper);

        /// <summary>
        /// 按轨道类型顺序创建 Preview Handler。
        /// </summary>
        public IReadOnlyList<ITrackPreviewHandler> CreatePreviewHandlers() =>
            modules.Where(module => module.PreviewFactory != null)
                .Select(module => module.PreviewFactory.Create()).ToArray();
        #endregion

        #region 内部辅助
        // 返回轨道声明的 TimelineTrack 元数据；未声明时返回空。
        private static TimelineTrackAttribute GetMetadataOrNull(Type type) =>
            type.GetCustomAttributes(typeof(TimelineTrackAttribute), false)
                .Cast<TimelineTrackAttribute>().SingleOrDefault();

        // 注册精确类型，避免扩展模块静默覆盖。
        private static void RegisterType(Dictionary<Type, TrackModule> index, Type type,
            TrackModule module, string role)
        {
            if (!index.TryAdd(type, module))
                throw new InvalidOperationException($"{role} 类型 {type.FullName} 已重复注册。");
        }

        // 解析精确类型模块，未知类型直接报告扩展缺失。
        private static TrackModule GetRequired(Dictionary<Type, TrackModule> index, Type type, string role)
        {
            if (type != null && index.TryGetValue(type, out TrackModule module)) return module;
            throw new InvalidOperationException($"未注册{role}类型：{type?.FullName ?? "<null>"}");
        }
        #endregion
    }
}
#endif
