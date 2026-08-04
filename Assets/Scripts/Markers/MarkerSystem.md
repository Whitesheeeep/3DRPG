# Marker Socket 系统使用指南

## 1. 职责与结构

Marker 系统把实例层级中的 Transform 暴露为类似 UE Socket 的稳定语义节点：

```text
TransformMarker
→ MarkerProvider.TryRebuild()
→ IMarkerProvider.TryGetMarker(MarkerKey)
→ VFX、装备或技能执行上下文取得 Transform
```

- `MarkerKey` 是可复用的 ScriptableObject 语义键，例如 `RightHand`、`WeaponTraceRoot`、`WeaponTraceTip`。
- `TransformMarker` 声明当前 Transform 对应哪个 Key。
- `MarkerProvider` 是实例级 Socket 容器，直接维护该作用域的 Marker 字典。
- `IMarkerProvider` 是运行时消费者依赖的只读查询接口。
- Config 不保存 Transform 层级路径或场景对象引用。

## 2. Provider 作用域

角色根、武器根和机关根可以分别挂载 `MarkerProvider`。Provider 只收集自己的作用域：

```text
Character（MarkerProvider）
├── RightHand（TransformMarker）
└── Weapon（MarkerProvider）
    ├── BladeRoot（TransformMarker）
    └── BladeTip（TransformMarker）
```

角色 Provider 不会收集嵌套武器 Provider 的 BladeRoot/BladeTip。因此不同武器可以复用相同 MarkerKey，不会在角色全局产生重复冲突。

同一 Provider 内：

- MarkerKey 不能为空。
- MarkerKey 必须唯一。
- 重建失败时保留上一份有效索引，并通过 `LastError` 暴露失败原因。
- Provider 会在 `Awake()`初次重建；换装或层级变化后必须由实例所有者再次调用 `TryRebuild()`。

## 3. 创建与配置

1. 在 Project 使用 `Create/RPG/Markers/Marker Key` 创建语义 Key。
2. 在角色、武器或其他实例根节点添加 `MarkerProvider`。
3. 在实际 Socket 节点添加 `TransformMarker`，并拖入对应 MarkerKey。
4. 若实例层级在运行时变化，完成变化后调用 Provider 的 `TryRebuild()`。

运行时查询示例：

```csharp
if (!markerProvider.TryGetMarker(targetKey, out Transform marker))
{
    // 当前实例没有提供该语义 Socket。
    return;
}
```

## 4. VFX 绑定

`VfxSkillClipConfig.MarkerKey`声明 VFX 使用的角色语义 Socket：

```text
VfxSkillClipConfig.MarkerKey
→ 角色根 MarkerProvider.TryGetMarker(...)
→ 得到 binding Transform
→ VFX Executor
```

- 空 MarkerKey 明确表示角色根节点。
- 非空 MarkerKey 只从角色根 Provider 查询，不进入嵌套武器 Provider。
- 当前角色没有对应 Key 时跳过该 VFX Clip，不回退到错误节点。

时间轴 VFX Inspector 仍为每个 Clip 单独显示“挂点”ObjectField。`FollowBinding`按当前帧跟随 Marker，`KeepWorldPosition`冻结 Clip 起始帧 Marker 世界矩阵。

## 5. WeaponTrace 绑定

`WeaponTraceAttackDetectionData`只保存刀刃插值采样点数量，不保存 MarkerKey、武器引用或层级路径。

运行时由装备系统选择当前武器 Provider，并把已解析的单刃 Transform 传入技能上下文：

```text
当前武器字典
→ 当前武器 MarkerProvider
→ 查询标准 WeaponTraceRoot / WeaponTraceTip Key
→ Root / Tip Transform
→ Skill WeaponTrace 执行上下文
```

不同武器拥有不同 Provider，但使用相同语义 Key。运行时字典决定传给技能的是哪一个武器实例；SkillConfig 不参与武器选择。

## 6. WeaponTrace Editor Preview

`EditorConfig`保存两个纯编辑器字段：

- `武器轨迹刀根 Key`
- `武器轨迹刀尖 Key`

它们只定义 Preview 使用的标准 Socket，不进入 SkillConfig 或运行时数据。

预览流程：

```text
预览角色副本
→ 重建全部 MarkerProvider
→ 遍历激活 Provider
→ 找到唯一同时提供刀根和刀尖 Key 的 Provider
→ 读取当前帧与上一采样帧 Transform
→ 绘制单刃扫掠线
```

未激活武器不参与匹配。零个或多个匹配 Provider、Key 为空或相同、Root/Tip 指向同一 Transform 时，只跳过 WeaponTrace 并在状态栏报告；Animation、VFX、Audio 和普通体积检测继续工作。

## 7. VFX 场景编辑代理

VFX 场景编辑继续使用不可保存的双代理结构：普通隐藏实例负责确定性预览，空 Transform 编辑代理负责局部 Transform 草稿。Inspector 提供“在场景中编辑”“选择编辑代理”“应用预览 Transform”和“取消场景编辑”。

应用时将代理世界矩阵转换回冻结 Marker 空间，通过 Document 产生一条 Undo。播放、Scrub、切换上下文或关闭窗口时销毁未应用代理。

## 8. 常见失败

- `根节点没有 MarkerProvider`：在需要公开 Socket 的角色或武器根添加 Provider。
- `TransformMarker 没有 MarkerKey`：为节点配置语义 Key。
- `同一 Provider 中存在重复 Key`：保留唯一节点；不同 Provider 之间允许复用。
- `找不到 VFX Marker`：确认该 Marker 位于角色根 Provider 作用域，而不是嵌套武器作用域。
- `WeaponTrace 没有匹配 Provider`：确认当前武器已激活，并配置了 EditorConfig 使用的刀根和刀尖 Key。
- `换装后查询不到新节点`：装备层级调整完成后重新调用对应 Provider 的 `TryRebuild()`。