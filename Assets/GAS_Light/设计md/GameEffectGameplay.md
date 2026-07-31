# Gameplay Effect 运行设计

## 数据边界

`GameplayEffectData` 是独立 ScriptableObject，只保存可复用配置。资产引用就是 GE 身份，不保存字符串 ID、Level 或运行状态。

运行时只使用一个 `GameEffectRuntime`：Instant 创建临时 Runtime 后立即释放；Duration 与 Infinite 把同一种 Runtime 加入目标 `GameEffectCtrl.ActiveEffects`。当前不拆分 Spec 和 Active，也不增加独立计算 Context。

```text
GameplayEffectData
        ↓ GameEffectCtrl 创建
GameEffectRuntime(Data、Source、Target、Level、StackCount、SetByCaller、计时)
        ↓ GameplayEffectModifier 计算
AttributeModifier List
        ↓
Target GameplayAttributeContainer
```

## GameplayEffectModifier

`GameplayEffectModifier` 同时保存 Attribute、运算类型、优先级和 Magnitude 计算策略。它读取 Source、Target 与 Runtime，直接生成不可变 `AttributeModifier`，不修改 Attribute、Tag 或 ActiveEffects。

- `FixedGameplayEffectModifier` 输出固定值。
- `LevelGameplayEffectModifier` 使用 `BaseMagnitude × LevelCurve(Level)`。
- `SetByCallerGameplayEffectModifier` 通过 GameplayTag Key 读取 Runtime 数据，缺失 Key 时应用失败。
- 自定义 Modifier 可以读取 Source/Target CurrentValue、Runtime.Level 和 Runtime.StackCount；一个配置只生成一个 AttributeModifier，多个目标使用多个配置项。

所有 Modifier 作者配置必须无运行时状态。层数不由框架自动放大；需要层数参与时，由具体子类明确读取 `Runtime.StackCount`，避免双重缩放。

### 失败边界与配置契约

GE 配置由 Editor 作者数据保证结构正确。Modifier 直接产出最终 `AttributeModifier`，Attribute、Magnitude、Resource/Stat 和 Owner 规则由 `GameplayAttributeContainer` 在一次原子提交中校验。

运行时只保留真正可能正常失败的入口：

- `GameEffectCtrl.TryApply`：目标 Tag 条件、必需 SetByCaller Key、GrantedTag 以及最终 Attribute 提交失败时返回 `false`。
- `GameEffectCtrl.TryRemove`：传入 Runtime 不属于当前 Target 时返回 `false`。
- `GameEffectRuntime.TryGetSetByCaller`：供外部可选查询；Modifier 使用已由 Controller 确认的必需 Key，不再逐层回退。

候选 Runtime 不是保底默认值，而是叠层事务的暂存状态。只有 AttributeContainer 成功提交整组 Modifier 后，Source、Level、StackCount、SetByCaller 和计时才会写回现有 Runtime，防止数值状态与 GE 状态分离。

## 生命周期

| DurationType | Period | 运行方式 |
|---|---:|---|
| Instant | 0 | Modifier 计算后批量 Instant 结算，不进入 ActiveEffects |
| Duration | 0 | 在有限时间内持有持续 Stat Modifier |
| Duration | > 0 | 有限时间内周期执行 Instant |
| Infinite | 0 | 持有持续 Stat Modifier，直到主动移除 |
| Infinite | > 0 | 持续周期执行 Instant，直到主动移除 |

Periodic 与 Duration 是两个独立维度：Infinite 表示没有自动到期时间，Period 表示存续期间定时结算。周期结果不创建持续 Modifier，Resource 伤害、治疗和回蓝每轮都走 Instant。

Runtime 移除时不重新计算 Magnitude，也不反向计算 Magnitude。`GameEffectRuntime` 本身实现 `IModifierSource`，Controller 按 Source 移除其全部持续 Modifier，再撤销 GrantedTags。

## 叠层

- None：每次应用创建独立 Runtime。
- AggregateBySource：相同 Data 且 Source 引用相同时合并。
- AggregateByTarget：同一 Target 上相同 Data 合并，并采用最近一次成功应用的 Source、Level 与 SetByCaller。

层数增加或成功溢出覆盖时，Controller 使用候选 Runtime 计算 Modifier；持续 GE 通过 AttributeContainer 原子替换原 Runtime 的整组 Modifier，成功后才提交 Source、StackCount 和计时。失败不会留下半更新 Runtime。

GrantedTags 按 Active Runtime 计数，而不是按 StackCount 计数。多个 Runtime 共享 Tag 时，由 `GameplayTagCountContainer` 保证移除其中一个不会提前清除标签。

Duration 到期策略只保留两种：`ClearEntireStack` 清除整个 Active Runtime；`RemoveSingleStackAndRefreshDuration` 每次移除一层，仍有层数时刷新完整 Duration。到期后不减少层数、只自动刷新 Duration 的策略等价于 Infinite，因此不提供；永久效果必须明确配置为 `Infinite`。

## 依赖方向

```text
AbilitySystemComponentBase
├─ GameplayTagCountContainer
├─ GameplayAttributeContainer
└─ GameEffectCtrl
   ├─ GameplayEffectData
   ├─ GameEffectRuntime : IModifierSource
   └─ GameplayEffectModifier
      └─ 直接输出不可变 AttributeModifier
```

配置资产不依赖 Editor Window；`GameEffectCtrl` 不依赖 View、Editor 或 Odin Tester。Odin Tester 仅在 `UNITY_EDITOR` 下调用真实公开 API。

## Editor 作者流程

GE Editor 作为 `GAS_SettingWindow` 的内嵌 MVC 页面，不创建独立 `EditorWindow`。左侧列出项目中的 `GameplayEffectData`，右侧直接使用 `SerializedObject` 绑定配置和选中的 managed-reference Modifier，不设置 Apply 按钮。字段 Enter 或失焦提交后，校验请求合并到下一次 Editor 更新执行，避免阻塞当前点击和拖放事件。

Effect 与 Modifier ListView 使用稳定 Model 引用列表作为 `itemsSource`，普通变化只调用 `RefreshItem/RefreshItems`。切换 Modifier 只重绑对应 managed-reference 详情；拖拽采用 Simple 模式，Drop 后只通过 `SerializedProperty.MoveArrayElement` 提交一次真实顺序，不重建整个页面。

Modifier 行通过 Attribute Editor 当前 Session Registry（没有 Session 时使用项目唯一 Registry）即时把稳定 AttributeId 解析为作者名称，ID 仅放在 Tooltip。名称不写入 GE，Attribute 重命名、Bake、Undo/Redo 或项目变化后重新解析；无效 ID 和 Registry 不明确都会显示明确诊断文本。

`GameplayEffectData.validationSets` 仅在 `UNITY_EDITOR` 下存在：非空时校验指定 `GameplayAttributeSet`，空列表则扫描项目全部 Set。每个 Set 都必须包含每个 Modifier 的目标 Attribute；非周期 Duration/Infinite 的持续 Modifier 还要求目标为 Stat。错误始终包含 Set 名称和资产路径。

Tag 不在 GE Validator 中重复校验。`TargetTagQuery`、`GrantedTags` 和 `SetByCallerGameplayEffectModifier.Key` 继续通过现有 `GameplayTagPropertyDrawer` 限定作者选择。

Modifier Add 菜单使用 `TypeCache` 发现可序列化的非抽象派生类型。一个 `GameplayEffectModifier` 仍只产生一个 `AttributeModifier`，整个 GE 的 Modifier 列表由 Runtime 汇总后原子提交。本阶段校验只在窗口中显示，不阻止 Play Mode 或 Build。
