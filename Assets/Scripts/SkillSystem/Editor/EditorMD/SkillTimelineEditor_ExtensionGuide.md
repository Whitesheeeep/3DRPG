# 技能时间轴编辑器扩展指南

## 1. 文档目的

本文记录技能时间轴新增轨道类型时需要扩展的位置，以及数据从运行时配置、Editor View、ViewModel 到 Document 的完整流转。角色挂点的配置、收集、运行时查询和 VFX 场景编辑方式见 [Marker 系统使用指南](../../../Markers/MarkerSystem.md)。

当前已经注册的模块顺序为：

```text
Animation → AttackDetection → VFX → Audio → Event
```

后续计划增加：

```text
State：技能状态标记
Custom Item Window：复杂 Item 的独立编辑窗口
```

扩展必须继续遵守以下边界：

- `SkillConfig` 保存运行时真正需要的数据，不保存窗口宽度、选择、折叠、滚动等编辑器状态。
- Canvas、Inspector 和 Custom Window 不直接修改 `SkillConfig`。
- 所有资产写入必须经过 `EditorViewModel → Document → ITrackDocumentHandler`，统一处理校验、Undo 和 Dirty。
- `TrackId` 与 `ItemId` 是稳定 GUID；不得用列表索引、显示名称或长期持有的 `SerializedProperty` 定位数据。
- 新轨道由 `TrackModuleRegistry` 注册，不在 Canvas、ViewModel 或 Inspector 中增加类型枚举和 `switch`。

## 2. 当前 TrackModule 架构

每种轨道由一个 `TrackModule` 聚合以下能力：

```text
TrackModule
├── ITrackProjection         Config → Group/Track/Item ViewData 与 Selection
├── ITrackDocumentHandler    SerializedObject 路由、创建、编辑和帧规则
├── ITrackDropHandler        可选；Project 素材 → IItemCreateRequest
├── IItemViewFactory         Item ViewData → 具体 UXML/View
├── IInspectorDrawer         Item ViewData → Inspector 与 IItemEditRequest
└── ITrackPreviewFactory     可选；创建窗口私有的轨道预览处理器
```

Module 的物理目录按契约与实现分离：

```text
Core/Modules/
├── Interface/    六类 Module 能力接口
└── Concrete/     Registry、Projection、Document、Drop、Item View、Inspector 与 Preview 实现
```

新增轨道时先在 `Interface` 中确认所需能力契约，再在 `Concrete` 的对应能力文件中增加实现；宿主 View、Document、CompositePreview 和输入 Controller 不放入 Module 目录。

`TrackModuleRegistry` 是编辑器内唯一的轨道能力注册表：

- 注册顺序决定分组显示顺序。
- 按具体 Group、Track、Item ViewData 和 Selection 类型查找模块。
- 未注册 `ITrackDropHandler` 的轨道自然拒绝 Project 素材拖入。
- `ElementFactory` 只实例化公共或指定路径的 UXML，不判断轨道类型。
- `Document` 只执行公共事务；具体序列化字段和业务规则由对应 Handler 提供。

## 3. 数据流转

### 3.1 打开 SkillConfig 与刷新显示

```text
Toolbar 选择 SkillConfig
→ EditorViewModel.OpenConfig(config)
→ Playback 停止并归零
→ Document.Open(config)
→ EditorViewModel 遍历 TrackModuleRegistry.Modules
→ 每个 ITrackProjection.CreateGroup(config)
→ 生成 GroupViewData / TrackViewData / ItemViewData
→ TimelineChanged、SelectionChanged、InspectorChanged
→ RowCollectionView 重建轨道行与 Item View
→ InspectorView 按具体 ViewData 从 Registry 获取 Drawer
```

ViewData 是只读显示投影。轨道只保存公共显示字段和 Item 列表；具体 Item ViewData 可保存对应 Config 的只读引用供 Inspector 展示，但不能直接改写它。

### 3.2 新增轨道和默认 Item

```text
Group Header “+”
→ EditorViewModel.AddTrack(group)
→ Registry.Get(group).Document
→ Document.AddTrack(handler)
→ Handler 提供轨道列表字段名与默认名称
→ Document 记录一条 Undo、写入 GUID、Apply、SetDirty
→ ContentChanged
→ ViewModel 重建投影并选择新 TrackId
```

```text
Track Header “+”
→ EditorViewModel.AddItem(track)
→ Registry.Get(track).Document
→ Document.AddItem(handler, track.Id)
→ Handler.InitializeItem(...)
→ Document 校验、排序并提交一次 Undo
→ ViewModel 重建投影并选择新 ItemId
```

因此 AttackDetection、State 等没有 Project 素材可拖入的轨道，仍可直接使用通用 `AddItem` 流程；Handler 负责给新 Item 填充类型专用默认值。

### 3.3 Project 素材拖入

```text
Project 素材拖到 Lane
→ TrackDragController 通过 Registry.TryGetDrop(track)
→ ITrackDropHandler.CanAccept(assets)
→ CoordinateMapper 将落点换算为整数帧
→ ITrackDropHandler.CreateRequest(assets, frame)
→ EditorViewModel.CreateItems(track, request)
→ 对应 ITrackDocumentHandler.CreateItems(...)
→ Document 在一次事务内校验并批量写入
→ 一条 Undo + 一次投影刷新
```

Drop Handler 只校验素材并创建请求，不持有 ViewModel，也不修改 Config。

### 3.4 Inspector 与 Canvas 编辑

```text
Inspector 字段变化
→ 具体 IInspectorDrawer 创建 IItemEditRequest
→ EditorViewModel.EditItem(item, request)
→ Registry 根据具体 ItemViewData 找到 TrackModule
→ Document.EditItem(handler, TrackId, ItemId, request)
→ Handler 校验请求类型并写入具体字段
→ Document 校验、Undo、Dirty、ContentChanged
→ ViewModel 重建投影
→ Canvas 与 Inspector 刷新权威数据
```

```text
Canvas 拖动或 Resize Item
→ ItemDragController 只维护视觉草稿
→ PointerUp 得到最终整数帧区间
→ EditorViewModel.MoveItem / ResizeItem
→ Document 使用模块 Handler 校验区间与同轨重叠
→ 成功：提交一条 Undo 并刷新
→ 失败或取消：恢复权威投影位置
```

### 3.5 Undo、重建和 Selection 恢复

```text
Unity Undo/Redo
→ Document 重新绑定 SerializedObject
→ ContentChanged
→ ViewModel 遍历 Module 重建全部投影
→ Registry 根据具体 Selection 类型找到 Projection
→ Projection 使用 TrackId / ItemId 重新定位 ViewData
→ Selection 仍存在则恢复，否则回到 None
→ Canvas 与 Inspector 刷新
```

### 3.6 Scene View 预览

```text
播放、跳帧或 Config 内容变化
→ EditorViewModel 通知 PlaybackController
→ PlaybackController 标记 Scrub / PlaybackStart / PlaybackAdvance
→ CompositePreview 准备不可保存的隔离角色副本
→ 按 Module 注册顺序调用 ITrackPreviewHandler.SampleFrame(context)
├── AnimationPreviewHandler：Animancer 姿势与绝对 Root Motion
├── VfxPreviewHandler：ParticleSystem 绝对时间模拟
└── AudioPreviewHandler：窗口私有 PlayableGraph 连续播放
```

`ITrackPreviewFactory` 是 Module 的无状态可选能力，负责为每个时间轴窗口创建独立 Handler。Handler 可以持有 VFX 实例、音频 Graph 或帧缓存，但不能访问 View、Selection、Document 或修改 SkillConfig。未注册 Preview Factory 的轨道会被自然跳过。

Animation Module 同时实现 `IPreviewActorPoseProvider` 和 `IPreviewActorBindingPoseProvider`。前者提供绝对帧 Root Motion，后者按每个 `VfxSkillClipConfig.MarkerKey` 临时采样任意帧的完整动画姿势并读取 Marker 世界矩阵，随后恢复当前播放头帧。VFX 不依赖 Animancer 或 Animation Handler 的具体类型：`FollowBinding` 使用当前帧的 Clip Marker，`KeepWorldPosition` 冻结该 Clip 起始帧 Marker；空 Key 使用角色根节点。MarkerKey 的创建、角色收集、Inspector 配置与运行时查询方式见 [Marker 系统使用指南](../../../Markers/MarkerSystem.md)。

Audio Preview 不接入运行时 `AudioManager`。每个窗口持有独立 `PlayableGraph`，通过 `AudioClipPlayable.SetSpeed()` 和 Mixer 输入权重表现 Pitch 与 Volume；Scrub 保持静音，开始播放或播放中重新定位时按当前帧源偏移重建 Voice。

新增 Preview 能力时继续在对应 TrackModule 注册 Factory/Handler，不在 `CompositePreview` 中增加轨道类型判断。AttackDetection 和 Event 尚未注册 Preview Factory。
## 4. 新增一种轨道时的扩展清单

新增轨道应按下面顺序完成，避免 Editor 层先引用尚未稳定的运行时数据。

### 4.1 运行时数据

1. 在 `SkillConfig` 增加显式强类型轨道列表及只读属性。
2. 定义 TrackConfig、ItemConfig 和必要枚举。
3. Track 继续使用 `SkillTrackHeader`；Item 保存独立 `id`。
4. 区间 Item 使用 `[StartFrame, EndFrame)`；Marker 使用单一 `frame`。
5. 仅编辑器字段使用 `#if UNITY_EDITOR`，运行时消费的状态不得包裹。

### 4.2 Document 与请求

1. 在 `DocumentFieldNames` 增加轨道列表、Item 列表和专用字段名。
2. 增加类型化 `CreateRequest`（仅素材批量创建需要）和 `EditRequest`。
3. 增加 `ITrackDocumentHandler` 实现，声明：
   - 轨道列表、Item 列表、起始帧和持续帧字段。
   - 是否支持 Resize。
   - 默认 Item 初始化、类型化创建、类型化编辑和全部专用字段复制规则。
4. 所有同轨 Item 均按半开区间执行互斥；Handler 必须在类型化创建或编辑写入前完成范围与专用字段校验，失败时返回 `EditResult`，不得部分写入。
5. `CopySpecificFields()` 必须复制该类型全部专用字段，并对 `SerializeReference` 等可变引用执行深复制，以支持复制和同模块跨轨道移动。
6. GUID 初始化与修复继续由 `EnsureStableIds()` 负责，不在内容变化后执行全量合法性扫描。
7. Handler 不缓存 `SerializedProperty`，每次操作都通过 TrackId/ItemId 重新查找。

### 4.3 投影、选择和 UI

1. 增加具体 GroupViewData、TrackViewData、ItemViewData。
2. 增加具体 GroupSelection、TrackSelection、ItemSelection。
3. 增加 `ITrackProjection` 实现，负责 Config 投影及 GUID 选择恢复。
4. 增加独立 Item UXML、USS、ItemView 和 `IItemViewFactory`。
5. 增加具体 `IInspectorDrawer`，字段变化只生成编辑请求。
6. 如可接收 Project 素材，增加 `ITrackDropHandler`；否则注册为 `null`。
7. 在 `TrackModuleRegistry.CreateDefault()` 注册一次完整 Module。
8. 后续 Preview/Runtime Player 按该轨道运行时语义增加消费者，不反向依赖 Editor 类型。

## 5. AttackDetection 轨道

AttackDetection 已实现为可 Resize 的区间 Clip，表达攻击检测在半开区间 `[StartFrame, EndFrame)` 内生效。

建议的最小运行时结构：

```text
SkillConfig.attackDetectionTracks
AttackDetectionTrackConfig
├── SkillTrackHeader header
└── List<AttackDetectionSkillClipConfig> clips

AttackDetectionSkillClipConfig
├── id
├── startFrame / durationFrames
├── sampleIntervalFrames
└── [SerializeReference] AttackDetectionDataBase detectionData
```

`detectionData` 当前支持 Box、Sphere、Capsule、Sector 和 WeaponTrace。普通体积 Config 只保存相对角色根的局部形状参数；WeaponTrace Config 只保存采样点数量，运行时由当前武器 MarkerProvider 解析标准刀根/刀尖 Socket 后把 Transform 传入技能上下文。两者都不保存绑定路径。

AttackDetection Module 已包含：

- `AttackDetectionProjection`、三层 ViewData 和三层 Selection。
- `AttackDetectionDocumentHandler`；支持 Resize，同轨区间不可重叠。
- `AttackDetectionEditRequest` 和 `AttackDetectionInspectorDrawer`。
- `AttackDetectionItemFactory`、独立 UXML/USS 和区间 Clip View。
- 默认不注册 Drop Handler，通过 Track Header “+”创建默认碰撞 Clip。
- Inspector 的 `IAttackDetectionDataDrawer` 注册表按具体配置类型绘制字段，主 Drawer 不判断具体形状。
- Type 切换通过 `AttackDetectionDataBase.Create(type)` 创建全新默认配置。
- 复制 Clip 时深拷贝 managed reference，修改 FPS 时同步重采样采样间隔。
- `AttackDetectionPreviewFactory / Handler` 在 Animation 采样之后收集当前有效 Clip，并通过 `SceneView.duringSceneGui` 绘制。
- `IAttackDetectionSceneDrawer` 按具体 DetectionData 类型注册 Box、Sphere、Capsule、Sector 和 WeaponTrace 的绘制策略；Handler 不判断 Type。
- 暂停或手动 Scrub 时，只有当前选中且仍有效的体积 Clip 显示 Handles；拖动期间使用本地草稿，MouseUp 后才提交一次 `AttackDetectionEditRequest`。
- 实际采样帧使用实色，间隔内的非采样帧使用弱化透明度；颜色由 `EditorConfig` 统一配置。
- WeaponTrace 当前只支持单刃；刀根和刀尖不进入 SkillConfig，由装备系统从当前武器 MarkerProvider 解析后传入运行时上下文。

AttackDetection 数据写入流转：

```text
Track “+”或 Inspector 修改
→ EditorViewModel
→ AttackDetectionDocumentHandler
→ Document 事务
→ SkillConfig.attackDetectionTracks
→ AttackDetectionProjection 重建
→ Canvas / Inspector / SceneView 预览刷新

Scene Handle Drag
→ AttackDetectionPreviewHandler 本地 DetectionData 草稿
→ MouseUp
→ IAttackDetectionSceneEditService.EditCommitted
→ EditorViewModel.EditItem
→ AttackDetectionEditRequest
→ Document / 一条 Undo

WeaponTrace Preview
→ EditorConfig 提供固定刀根 / 刀尖 MarkerKey
→ PreviewActorInstance 查找唯一匹配的激活 MarkerProvider
→ AnimationPreviewHandler 临时采样上一采样帧 Transform
→ 恢复当前帧
→ WeaponTraceSceneDrawer 绘制单刃前后姿态与插值扫掠线
```

## 6. State 轨道与 Custom Item Window

状态数据应使用强类型 State Module，不复用通用 Event 的字符串参数作为最终结构。

若状态表示霸体、无敌、输入锁定、移动锁定等持续效果，建议使用区间 `StateSkillClipConfig`；若状态只表示某帧发送进入、退出或切换命令，则使用单帧 Marker。最终形态需要在状态运行时接口确定后选择，也可以拆成两个独立 Module。

简单字段仍在右侧 Inspector 编辑；只有状态参数结构较复杂时才打开独立 Custom Item Window。该窗口属于 View 层扩展，不是新的数据写入入口。

建议为 `TrackModule` 增加可选的 Custom Editor 能力：

```text
TrackModule
└── IItemEditorLauncher（可选）
```

Custom Window 必须使用稳定 GUID，并跟随宿主时间轴生命周期：

```text
双击 State Item / Inspector 点击“详细编辑”
→ Registry 查询该 Module 的 IItemEditorLauncher
→ 使用 TrackId + ItemId 打开 StateItemEditorWindow
→ Window 从最新 ViewData 构建编辑草稿
→ 用户确认
→ 生成 StateEditRequest
→ 宿主 EditorViewModel 按 TrackId + ItemId 提交
→ StateDocumentHandler
→ Document 校验、Undo、Dirty、ContentChanged
→ 主窗口重建投影
→ Custom Window 重新读取权威数据
```

Custom Window 的约束：

- 不保存或长期持有 `SerializedProperty`。
- 不直接调用 `Undo.RecordObject` 或修改 `SkillConfig`。
- 编辑期间使用草稿；确认时只提交一次请求和一次 Undo。
- 宿主切换 SkillConfig、删除目标 Item 或关闭时间轴窗口后，Custom Window 应关闭或进入只读失效状态。
- 取消操作不产生资产变更。
- 如果窗口允许在未选中 Item 时继续编辑，ViewModel 需要增加按 `TrackId + ItemId` 提交的重载，不能依赖当前 Selection。

## 7. 验收基线

- 新模块只在 Registry 注册一次，Canvas、ViewModel、InspectorView 不出现具体轨道类型判断。
- 新增、删除、移动、Resize、Inspector 编辑与 Custom Window 编辑均经过 Document。
- 一次语义操作只产生一条 Undo；非法区间或非法参数不会留下部分写入。
- Undo/Redo、轨道重排和投影重建后，Selection 能通过 GUID 恢复。
- 新 Item 的 UXML 可由 UI Builder 打开，视觉尺寸和颜色只定义在 USS。
- AttackDetection Preview 只绘制形状和编辑配置，不执行 Physics 查询；State 尚未接入时仍可独立扩展。
- 新增类型、公开方法、非公开方法和复杂类 Region 遵循根 `AGENTS.md` 的中文注释规范。

## 8. 尚待确定的运行时决策

- AttackDetection 的伤害、阵营、重复命中和过滤数据归属。
- State 使用区间 Clip、单帧 Marker，还是拆成两种 Module。
- Custom Window 编辑的是单个 State Item，还是独立的状态定义资产。
- State 在 Preview 中只绘制，还是需要执行无副作用模拟；AttackDetection 当前固定为只绘制。
