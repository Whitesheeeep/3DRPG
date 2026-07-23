# 技能时间轴编辑器扩展指南

## 1. 文档目的

本文记录技能时间轴新增轨道类型时需要扩展的位置，以及数据从运行时配置、Editor View、ViewModel 到 Document 的完整流转。

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
└── IInspectorDrawer         Item ViewData → Inspector 与 IItemEditRequest
```

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
   - 是否要求同轨区间互斥。
   - 默认 Item 初始化、类型化创建和类型化编辑规则。
4. 将 GUID、范围、排序和专用字段校验接入 `ContentValidator`。
5. Handler 不缓存 `SerializedProperty`，每次操作都通过 TrackId/ItemId 重新查找。

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

`detectionData` 当前支持 Box、Sphere、Capsule、Sector 和 WeaponTrace。Config 只保存局部形状参数；角色、武器、刀根和刀尖等检测基准由未来运行时调用方传入，不保存绑定路径。

AttackDetection Module 已包含：

- `AttackDetectionProjection`、三层 ViewData 和三层 Selection。
- `AttackDetectionDocumentHandler`；支持 Resize，同轨区间不可重叠。
- `AttackDetectionEditRequest` 和 `AttackDetectionInspectorDrawer`。
- `AttackDetectionItemFactory`、独立 UXML/USS 和区间 Clip View。
- 默认不注册 Drop Handler，通过 Track Header “+”创建默认碰撞 Clip。
- Inspector 的 `IAttackDetectionDataDrawer` 注册表按具体配置类型绘制字段，主 Drawer 不判断具体形状。
- Type 切换通过 `AttackDetectionDataBase.Create(type)` 创建全新默认配置。
- 复制 Clip 时深拷贝 managed reference，修改 FPS 时同步重采样采样间隔。

AttackDetection 数据写入流转：

```text
Track “+”或 Inspector 修改
→ EditorViewModel
→ AttackDetectionDocumentHandler
→ Document 事务
→ SkillConfig.attackDetectionTracks
→ AttackDetectionProjection 重建
→ Canvas / Inspector / 未来 SceneView 预览刷新
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
- AttackDetection、State 的运行时播放器和 Preview 尚未接入时，编辑器仍可稳定保存、显示和修改数据。
- 新增类型、公开方法、非公开方法和复杂类 Region 遵循根 `AGENTS.md` 的中文注释规范。

## 8. 尚待确定的运行时决策

- AttackDetection 的伤害、阵营、重复命中和过滤数据归属。
- State 使用区间 Clip、单帧 Marker，还是拆成两种 Module。
- Custom Window 编辑的是单个 State Item，还是独立的状态定义资产。
- AttackDetection 和 State 在 Preview 中只绘制，还是需要执行无副作用模拟。
