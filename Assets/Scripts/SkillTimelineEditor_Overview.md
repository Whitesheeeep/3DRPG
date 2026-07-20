# 技能时间轴编辑器总览

## 1. 目标与当前决定

制作一个类似 Unity Animation 窗口的自定义技能时间轴编辑器，用于编辑动画、特效和技能事件，并为后续在固定预览场景中播放技能做好准备。

当前已确定：

- 使用 Unity UI Toolkit、UXML、USS 和私有 MVVM 架构实现 EditorWindow。
- 时间轴使用整数帧，技能默认 `30 FPS`，每个技能可单独修改 FPS。
- `SkillConfig` 直接保存显式具体类型的轨道列表，不使用抽象 Track 和多态根列表。
- `AnimationClip`、VFX Prefab 等资源由用户直接拖拽引用，V1 不考虑 ResSystem、Addressables 或二次资源加载。
- 运行时直接消费已经加载的 `SkillConfig`，只创建播放会话和游标状态，不生成、保存或重新加载第二份 Runtime 资产。
- 预览场景与演示角色属于编辑器固定设置，不写入 `SkillConfig`；切换技能不需要重新选择。
- `SkillTimelineEditorConfig` 只保存窗口尺寸、缩放、滚动策略与 IMGUI 绘制参数，不混入 FPS、总帧、轨道或资源等运行时技能数据。
- 动画、特效、事件等同类轨道在窗口中分组显示；分组不是运行时业务数据。
- V1 不实现节点编辑器、伤害系统、Buff、资源热更新和运行时倒放。

## 2. Window 总览

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ 新建/选择 SkillConfig │ FPS 30 │ 总帧 90 │ 裁剪到内容                     │
├──────────────────────────────────────────────────────────────────────────────┤
│ 编辑器场景 [SkillPreview.unity] [加载场景]                                  │
│ 演示角色   [Knight_Player.prefab / 场景 GameObject]                         │
├──────────────────────────────────────────────────────────────────────────────┤
│ [播放/暂停] [停止] [上一帧] [下一帧] │ 当前帧 18 │ 缩放 ─────○─────       │
├──────────────┬───────────────────────────────────────────────┬───────────────┤
│ 轨道/分组     │ 0     10     20     30     40 ...             │ Inspector     │
├──────────────┼───────────────────────────────────────────────┤               │
│ ▼ 动画配置  + │               │ 当前帧指示线                  │ Group/Track/  │
│   角色动画     │ [ Sword_Slash_A                    ]         │ Clip/Marker   │
├──────────────┼────────────────│──────────────────────────────┤ 专用属性      │
│ ▼ 特效配置  + │               │                              │               │
│   刀光特效     │          [ VFX_FireSlash ]                   │               │
│   命中特效     │                [ VFX_FireHit ]               │               │
├──────────────┼────────────────│──────────────────────────────┤               │
│ ▼ 事件配置  + │               ◆ Damage  ◆ CameraShake       │               │
│   战斗事件     │               │                              │               │
└──────────────┴────────────────┴──────────────────────────────┴───────────────┘
```

### 2.1 顶部配置区

- `SkillConfig ObjectField`：选择或新建当前编辑的技能配置。
- `FPS`：默认 30；修改时保持实际时间并重采样所有帧位置。
- `总帧数`：显式保存；内容拖出边界时自动扩展。
- `裁剪到内容`：缩短到最后一个 Clip 结束帧或 Marker 所在帧。
- `编辑器场景`：项目级固定预览场景。
- `演示角色`：接受 Prefab 或预览场景中的 GameObject。
- `加载场景`：确认当前场景的未保存修改后，通过 `EditorSceneManager` 切换到固定预览场景；V1 不解析、实例化或采样演示角色。

### 2.2 播放与时间轴

- 提供播放/暂停、停止、上一帧、下一帧和当前帧输入。
- 点击标尺、拖动播放头、上一帧和下一帧都只进行无副作用采样，不触发伤害等一次性运行时事件。
- 播放时按 Editor Update 推进，并根据 Config FPS 计算当前整数帧。
- 当前帧指示线从标尺贯穿所有分组头和轨道到底部；左侧轨道头与右侧 Inspector 不属于帧坐标区域，因此不绘制指示线。
- 时间轴区域使用右侧双向 `ScrollView`：普通滚轮纵向移动，`Shift + 滚轮` 横向移动，`Ctrl + 滚轮` 或缩放滑块缩放；水平与垂直滚动条始终显示。
- 左侧轨道标签使用隐藏滚动条的纵向 ScrollView，右侧时间内容使用双向 ScrollView；两侧真实纵向偏移双向同步，Ruler、播放头和 Lane 共享右侧水平坐标。
- Clip 拖动和左右裁剪全部吸附整数帧。同一轨道内的区段不可重叠，并发内容使用多条同类型轨道。
- 空 SkillConfig 时仍提供 Editor Config 定义的虚拟可滚动画布和播放头 Scrub；选择技能后恢复技能总帧边界。

### 2.3 分组与 Inspector

- 固定分组顺序为动画、特效、事件；同类 Track 只能在本组内重排。
- 分组支持折叠和新增对应类型轨道；折叠、滚动和缩放属于窗口状态。
- Inspector 根据当前选择切换 Drawer：
  - Group：组名称、轨道数量和新增轨道入口。
  - Track：名称、静音、锁定及该轨道特有配置。
  - Animation Clip：动画引用、起始帧、持续帧、源动画偏移和播放速度。
  - VFX Clip：Prefab、起始/持续帧、挂点、Follow/Stop 模式和局部变换。
  - Event Marker：触发帧、事件类型名、显示名称和参数文本。
- Inspector 通过统一 `ISkillTimelineInspectorDrawer` 分派具体 Drawer，主 View 不判断 Odin 或 Unity 原生 PropertyField 的实现细节。

## 3. 数据结构

### 3.1 SkillConfig

```csharp
[CreateAssetMenu(menuName = "RPG/Skill/Skill Config")]
public sealed class SkillConfig : ScriptableObject
{
    [SerializeField, Min(1)] private int frameRate = 30;
    [SerializeField, Min(1)] private int durationFrames = 1;

    [SerializeField] private List<AnimationTrackConfig> animationTracks = new();
    [SerializeField] private List<VfxTrackConfig> vfxTracks = new();
    [SerializeField] private List<EventTrackConfig> eventTracks = new();
}
```

具体列表使 Unity 原生序列化、Undo、Inspector 和类型迁移更稳定，也与窗口的固定类型分组一致。新增轨道类型时显式扩展 `SkillConfig` 和对应 Drawer。

### 3.2 公共轨道与区段规则

```csharp
[Serializable]
public sealed class SkillTrackHeader
{
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField] private bool muted;

#if UNITY_EDITOR
    [SerializeField] private bool editorLocked;
    [SerializeField] private Color editorColor;
#endif
}
```

- Config、Track、Clip、Marker 使用独立 GUID，不依赖 Unity managed reference ID。
- 区段统一使用半开区间 `[StartFrame, EndFrame)`，其中 `EndFrame = StartFrame + DurationFrames`。
- Track 内的 Clip 按 `StartFrame` 排序且不可重叠；不同 Track 可以并发。
- Marker 的合法范围为 `0..DurationFrames - 1`，同帧允许多个 Marker。
- `muted` 会影响预览和未来运行时播放，因此保留在运行时数据中。
- 锁定、编辑颜色等纯编辑状态使用 `#if UNITY_EDITOR` 包裹。

### 3.3 具体轨道

```csharp
[Serializable]
public sealed class AnimationTrackConfig
{
    [SerializeField] private SkillTrackHeader header;
    [SerializeField] private List<AnimationClipConfig> clips = new();
}

[Serializable]
public sealed class VfxTrackConfig
{
    [SerializeField] private SkillTrackHeader header;
    [SerializeField] private List<VfxClipConfig> clips = new();
}

[Serializable]
public sealed class EventTrackConfig
{
    [SerializeField] private SkillTrackHeader header;
    [SerializeField] private List<SkillEventMarkerConfig> markers = new();
}
```

V1 字段约定：

- `AnimationClipConfig`：ID、AnimationClip、StartFrame、DurationFrames、SourceStartFrame、PlaybackSpeed。
- `VfxClipConfig`：ID、Prefab、StartFrame、DurationFrames、BindingPath、局部位置/旋转/缩放、FollowMode、StopMode。
- `SkillEventMarkerConfig`：ID、Frame、EventTypeName、DisplayName、ParameterText。

事件首版保留“类型名 + 参数文本”，等战斗事件体系确定后再评估强类型载荷迁移。

## 4. Editor 专用配置与固定设置

`SkillTimelineEditorConfig.asset` 是窗口表现参数的单一项目资产：保存最小窗口尺寸、缩放范围、滚轮策略、内容留白、Marker 宽度以及 Ruler/Grid/Playhead 的 IMGUI 绘制参数。它位于 `Editor` 目录，只由 `SkillTimelineEditorWindow` 加载并注入各表现组件，不进入运行时构建，也不保存当前 `SkillConfig`、FPS、总帧、轨道、Clip、Marker、预览资源或播放状态。

预览场景和演示角色不进入 `SkillConfig`，由 Editor 专用设置保存：

```csharp
#if UNITY_EDITOR
[FilePath("ProjectSettings/SkillTimelineEditorSettings.asset",
    FilePathAttribute.Location.ProjectFolder)]
public sealed class SkillTimelineEditorSettings
    : ScriptableSingleton<SkillTimelineEditorSettings>
{
    [SerializeField] private string previewSceneGuid;
    [SerializeField] private string previewActorGlobalObjectId;
}
#endif
```

- 场景保存 Asset GUID，移动或重命名场景后仍可通过 AssetDatabase 定位。
- 演示角色保存 `GlobalObjectId` 字符串，兼容 Prefab 资产和场景 GameObject。
- V1 只恢复和显示场景/角色字段；角色解析、Prefab 实例化与采样留给后续 `ISkillTimelinePreview` 实现。
- 修改设置时调用 `Save(true)`；窗口打开和 Domain Reload 后自动恢复 ObjectField。
- 当前选中项、播放帧、组折叠、缩放与滚动属于窗口会话状态，可使用 `SessionState` 保存，不写入项目设置。

## 5. 编辑器架构

```text
SkillTimelineEditorWindow
  ├── 创建/释放 SkillTimelineEditorViewModel
  ├── 加载 UXML
  └── 处理 Domain Reload 与 Editor Update

SkillTimelineEditorView
  ├── SkillTimelineToolbarView
  ├── SkillTimelineCanvasView（仅组合子 View/Controller）
  │   ├── SkillTimelineRowCollectionView + UXML 元素工厂
  │   ├── SkillTimelineItemDragController / SkillTimelineScrubController
  │   ├── SkillTimelineViewportInputController
  │   └── Ruler / 单一 Grid / Playhead IMGUI 绘制 View
  └── SkillTimelineInspectorView

SkillTimelineEditorViewModel : IViewModel
  ├── 当前帧、播放状态和具体 Selection
  ├── 面向 View 的意图方法
  └── Changed / SelectionChanged / PlaybackChanged 等通知

SkillTimelineEditorDocument
  ├── 当前 SkillConfig 与 SerializedObject
  ├── 轨道和 Clip 修改、排序与校验
  └── Undo、Dirty、保存和 Undo/Redo 后重建投影
```

- 主布局定义在 UXML，颜色、尺寸、滚动、选中/锁定/静音状态放在 USS。
- Group Header、Track Header、Lane、Clip 与 Marker 由独立 UXML 模板创建；Canvas 不直接创建动态元素。
- 缩放、滚动、Pointer Capture 与拖拽草稿属于表现层；ViewModel 只接收已经吸附后的整数帧语义命令。
- 所有 Config 修改经过 Document，使用 `Undo.RecordObject`、`SerializedObject.Update()`、`ApplyModifiedProperties()` 和 `EditorUtility.SetDirty()`。
- ViewModel 不保存 VisualElement、Button、GameObject 预览实例或其他 UI 控件。

## 6. 预览与运行时方向

### 6.1 编辑器预览

- V1 仅定义 `ISkillTimelinePreview` 接口，不创建实际 Preview 实现。
- 播放、Seek 和前后帧在 Preview 为空时仍正常更新播放头。
- 后续 Animancer、VFX 与事件采样通过接口实现接入，不修改 Window、Canvas 或 Document。
- V1 不实例化演示角色、特效，也不执行事件。

### 6.2 V1 运行时

```text
已加载的 SkillConfig
        ↓ Play(config, context)
SkillTimelinePlaySession
  - 当前帧/上一帧
  - 各轨道游标
  - 活动动画与特效实例
        ↓
Animation / VFX / Event Sink
```

- Player 不重新加载 SkillConfig、AnimationClip 或 Prefab。
- 播放会话只保存可变游标与活动状态，共享 Config 始终只读。
- 正向推进触发区间为 `(PreviousFrame, CurrentFrame]`，单次 Update 跨多帧时补触发全部 Marker。
- 跨过完整短 Clip 时仍按顺序执行 Enter 和 Exit。
- Stop 必须终止动画并通过 `PoolManager` 或对应 Preview Sink 回收所有活动 VFX。
- V1 不支持运行时倒放和默认循环；循环若以后需要，由播放请求决定。

## 7. 校验与验收

- 切换多个 SkillConfig 时，预览场景和演示角色保持不变。
- Domain Reload、关闭并重开窗口后，项目级预览设置正确恢复。
- FPS、总帧数、ID、资源引用、区段范围、同轨重叠和列表排序均可校验。
- 缩放和横向滚动后，标尺、Clip、Marker 与当前帧指示线保持同一帧坐标。
- 播放、暂停、停止、上一帧、下一帧和拖动播放头行为一致。
- 当前帧指示线连续贯穿所有可见组头和轨道。
- Group、Track、Clip、Marker 的 Inspector 正确切换。
- 新增、删除、重排、拖动、裁剪、修改属性和 FPS 重采样均支持 Undo/Redo，并立即刷新窗口。
- UXML 可由 UI Builder 打开，USS 在 UXML 中引用，Unity Console 无 C#、UXML 或 USS 错误。

## 8. 后续再讨论

- 动画淡入淡出、Animancer Layer、AvatarMask 与 Additive 混合。
- 音效、位移、伤害、Buff 等新轨道类型。
- Event 参数由文本升级为强类型载荷。
- 编辑器 Scene 预览的相机控制、角色朝向与目标假人。
- 多选、框选、批量移动、复制粘贴和快捷键。
- Addressables、远程资源和离线运行时烘焙。
