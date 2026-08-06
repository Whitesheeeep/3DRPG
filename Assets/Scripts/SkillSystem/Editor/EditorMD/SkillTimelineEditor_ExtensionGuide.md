# 技能时间轴编辑器扩展指南

## 1. 当前架构

技能资产采用“根资产 + 有序 Track 子资产”结构：

```text
SkillConfig
└── List<TrackConfigBase> Tracks
    ├── AnimationTrackConfig
    ├── AttackDetectionTrackConfig
    ├── VfxTrackConfig
    ├── AudioTrackConfig
    └── EventTrackConfig
```

`SkillConfig.Tracks` 的顺序就是时间轴物理行顺序，不再存在 Group、Projection 或 ViewData。`TrackConfigBase` 直接保存稳定 ID、静音状态以及 Editor 下的名称、锁定和颜色；具体 Track 保存自己的 Item 列表。Item 继承 `TimelineItemConfigBase`，统一提供 ID 和半开帧区间 `[StartFrame, EndFrame)`。

轨道是 `SkillConfig` 的隐藏 `ScriptableObject` 子资产。删除轨道时必须同时移除根列表引用并销毁子资产；Undo 必须同时记录根资产和相关 Track 子资产。

角色挂点、武器 Socket 和 VFX Marker 的使用方式见 [Marker 系统使用指南](../../../Markers/MarkerSystem.md)。

## 2. TrackModule 能力

每个可扫描轨道由 `[TimelineTrack]` 和一个 `TrackModuleDefinition` 组成：

```text
[TimelineTrack("动画轨道", order: 0)]
AnimationTrackConfig
        │
        └── AnimationTrackModuleDefinition
            └── TrackModule
                ├── ITrackDocumentHandler
                ├── ITrackDropHandler（可选）
                ├── IItemViewFactory
                ├── IInspectorDrawer
                └── ITrackPreviewFactory（可选）
```

`TrackModuleRegistry` 使用 `TypeCache` 扫描具体 `TrackConfigBase` 和 `TrackModuleDefinition`，要求二者按 Track 类型一对一匹配。Attribute 的 `order` 只决定右键菜单顺序和“按轨道类型重排”的顺序；新建 Track 默认追加到列表末尾。

接口位于 `Core/Modules/Interface`，实现位于 `Core/Modules/Concrete`。Canvas、ViewModel、Document 和原生 Inspector 不包含具体轨道类型的 `switch`。

## 3. 数据流转

### 3.1 打开与显示

```text
Toolbar 选择 SkillConfig
→ EditorViewModel.OpenConfig
→ Document.Open
→ CanvasController 读取 Config.Tracks
→ RowCollectionView 为每个 TrackConfigBase 创建 Header 与 Lane
→ IItemViewFactory 使用实际 Item Config 创建 ItemView
```

Track Header、Lane 和 ItemView 只保留实际 Config 引用，不保存显示字段副本。布局草稿、Pointer Capture、缩放和滚动仍只属于 Canvas 表现层。

### 3.2 面板右键新增与重排

```text
轨道面板空白处右键
→ TrackPanelContextMenuController
→ Registry.Modules 按 TimelineTrack.order 生成“添加轨道”菜单
→ EditorViewModel.AddTrack(module)
→ Document.AddTrack
→ 创建 TrackConfigBase 子资产并追加 Config.Tracks
→ 一条 Undo + TimelineChanged
```

“按轨道类型重排”会按 Attribute 顺序稳定排序，同类型 Track 保持原相对顺序。Header 的上移/下移按钮只移动一条物理行，可以跨越不同 Track 类型。

### 3.3 Item 创建、编辑和跨轨拖动

```text
Header “+”或 Project 素材拖入
→ EditorViewModel
→ 对应 ITrackDocumentHandler
→ Track 子资产的 SerializedObject
→ 区间校验、Undo、Dirty
→ ContentChanged
```

```text
Item 水平或垂直拖动
→ ItemDragController 维护视觉草稿
→ PointerUp
→ 同 Lane：Document.MoveItem
→ 同类型其他 Lane：Document.MoveItemToTrack
→ 目标类型、锁定和区间再次校验
→ 一条跨 Track Undo
```

Resize 不允许跨 Lane。跨轨移动保留 Item GUID、帧区间和全部类型专用字段；`SerializeReference` 数据必须由 Handler 深复制。

### 3.4 Selection 与 Unity 原生 Inspector

Selection 只保存稳定 `TrackId` 和可选 `ItemId`：

```text
点击 Track 或 Item
→ EditorViewModel.Select...
→ TimelineWindow 成为 Selection.activeObject
→ TimelineWindowInspector
→ window.SelectedData（实际 Track/Item Config）
→ Registry.GetInspector(data)
→ Drawer 创建类型化 EditRequest
→ ViewModel → Document → Undo
```

原生 Inspector 不复制字段，也不直接写 Config。用户选择其他 Project/Scene 对象后，时间轴不会抢回 Inspector；再次点击时间轴内容时才重新选择窗口。Inspector 锁定由 Unity 自己处理。

Undo/Redo 后，Document 重建根 `SerializedObject`，ViewModel 根据 GUID 在当前 `Config.Tracks` 和 `Track.Items` 中恢复 Selection。

### 3.5 Preview

```text
PlaybackController.SampleFrame
→ CompositePreview
→ 按 TrackModule 顺序调用 Preview Handler
├── AnimationPreviewHandler
├── AttackDetectionPreviewHandler
├── VfxPreviewHandler
└── AudioPreviewHandler
```

Preview Handler 直接从 `SkillConfig.Tracks.OfType<TTrack>()` 读取对应 Track。Handler 可以持有窗口私有缓存、VFX 实例或 Audio Graph，但不能修改 Config、Document 或 UI。

## 4. 新增一种轨道

例如新增 State Track：

1. 新增 `StateTrackConfig : TrackConfigBase`，添加 `[TimelineTrack("状态轨道", order: ...)]`。
2. 新增 `StateItemConfig : TimelineItemConfigBase`，保留稳定 ID 与帧语义。
3. 新增类型化 Create/Edit Request。
4. 新增 `StateDocumentHandler`，声明 Track 类型、Item 列表字段、帧字段、初始化、复制和编辑规则；不得缓存 `SerializedProperty`。
5. 新增 `StateItemFactory`、Item UXML/USS 与具体 ItemView。
6. 新增 `StateInspectorDrawer`；字段提交经 ViewModel 和 Document。
7. 如支持 Project 素材拖入，新增 `StateDropHandler`；否则 Module 的 Drop 传空。
8. 如需要预览，新增 `StatePreviewFactory/Handler`。
9. 新增 `StateTrackModuleDefinition`，将上述能力组合为 `TrackModule`。

完成后不需要修改 `SkillConfig`、Canvas、RowCollectionView、EditorViewModel、TimelineWindowInspector 或 Track 面板菜单。

## 5. 约束

- `SkillConfig` 只保存运行时数据；窗口尺寸、缩放、滚动和选择不进入资产。
- Track ID 与 Item ID 是稳定 GUID，不使用名称或列表索引作为持久定位。
- 所有写入经过 Document；View 和 Drawer 不直接修改 Config。
- 同一 Track 的 Item 半开区间互斥，Event Marker 视为 `[Frame, Frame + 1)`。
- `ITrackDocumentHandler.TrackType` 是 Handler 路由依据，不能根据重复的 `clips` 字段名猜测类型。
- 类型重排只改变统一 Track 列表，不改变 Track 子资产内容。
- Track 删除必须同时删除列表引用和子资产，避免孤立子资产。
## 9. Item 与 Track 拖拽表现

Item 的 Move 交互使用三层表现，且拖动期间不修改配置：

```text
PointerDown
→ 源 Item 保留在权威帧并弱化
→ ItemDragPreviewView 通过对应 Module ItemFactory 创建同类型 Ghost 与 Placeholder

PointerMove
→ Ghost 保持鼠标抓取偏移
→ ItemDragController 命中 Lane 并换算最近整数帧
→ Document.CanMoveItem / CanMoveItemToTrack 只读校验
→ Placeholder 在目标 Lane 内容坐标显示有效或无效落点

PointerUp
→ 有效落点提交一次 MoveItem / MoveItemToTrack
→ Document 创建一条 Undo
→ TimelineChanged 重建权威位置
```

Resize 不创建 Ghost 或 Placeholder，仍只修改当前 Item 的本地裁剪草稿，松手后提交一次。

Track Header 的第一行包含专用拖拽把手与名称，第二行包含只读的具体 Track 子资产 `ObjectField` 和操作按钮。`ObjectField.objectType` 使用 Track 的实际类型，不能替换引用；`SkillConfig.Tracks` 同样在 Odin Inspector 中只读，结构只能由时间轴窗口修改。

```text
Track 把手 PointerDown
→ 固定标题行、Lane 背景与 Item Lane 同步弱化
→ 左右内容区显示同一插入边界

PointerMove
→ 按行中点计算 0..TrackCount 插入索引
→ 靠近标签 ScrollView 上下边缘时通过 CanvasModel.ScrollOffset 自动滚动

PointerUp
→ EditorViewModel.MoveTrack(track, insertionIndex)
→ Document.MoveTrackToIndex
→ 移除源索引后修正目标索引
→ 有效变化只产生一条 Undo
```

轨道锁定只限制 Item 编辑，不限制轨道自身排序。Header 的上移、下移按钮与拖拽排序共用 `MoveTrackToIndex()`；删除轨道仍同时移除根列表引用并销毁该 Track 子资产。