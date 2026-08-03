# Gameplay Ability 第一阶段

## 职责边界

第一阶段流程为“授予 Spec → 校验 Source Tag → 提交 Cooldown 与 Cost → 创建 Ability Runtime → 接收外部 Targeting 结果 → 应用 SelfEffects 与 TargetEffects → 外部 End 或 Cancel”。

范围、碰撞、阵营、目标筛选、投射物和空间信息属于外部 Targeting。GA 不保存目标，也不自动移除自己应用的 Gameplay Effect。

## 作者数据

GameplayAbilityData 是独立 ScriptableObject，保存 ActivationTagQuery、CostEffect、CooldownEffect、SelfEffects 与 TargetEffects。

- ActivationTagQuery 使用 Source 的 TagCountContainer 检查 All/Any/None。
- CostEffect 必须为 Instant。
- CooldownEffect 必须为 Duration 或 Infinite。
- SelfEffects 以 Source 同时作为 Source 和 Target 应用。
- TargetEffects 对外部提供的每个 Target 分别应用。

Ability 允许没有 SelfEffects 和 TargetEffects，此时可只负责 Cost/Cooldown 提交。

## Spec、Handle 与 Runtime

每个 GameplayAbilityCtrl 单调分配 Handle，0 为非法值且不会复用。GameplayAbilitySpec 是 ASC 中长期存在的授予状态，保存 Data 和当前 Level。Level 只能通过 Controller 修改；有 Active Runtime 的 Spec 不允许移除。

激活成功后创建 GameplayAbilityRuntime，并复制 Spec 当前 Level、Source 和 SetByCaller。之后修改 Spec Level 或调用方字典不会改变已经激活的 Runtime。

Runtime 状态只允许 Active 转为 Ended 或 Cancelled。每个 Runtime 第一阶段只允许执行一轮 Effects。同一个 Spec 可同时持有多个 Active Runtime，但 Cooldown Effect 存在时会阻止再次激活。

## 激活事务

激活顺序固定为：检查 Spec 与 Level、ActivationTagQuery、Cooldown 是否已存在、应用 Cooldown、应用 Cost、创建 Runtime。

Cooldown 先提交；Cost 被 Attribute Pre 或 GE 配置拒绝时，Controller 精确移除本次刚创建的 Cooldown Runtime。失败不会创建 Ability Runtime。Cost、Cooldown、SelfEffects 和 TargetEffects 都使用 Runtime 的 Level 与 SetByCaller。

## Effect 执行和生命周期

TryExecuteEffects 的 bool 表示 Runtime 与目标调用契约合法，并且这一轮流程已完成。某个 Target 拒绝某个 GE 不会中断其他 Target。

Instant GE 没有 Active Runtime。Duration 与 Infinite GE 的 Runtime 会返回给调用方；GA Controller 不保存这些对象。外部若需要在技能结束、动画中断或其他时机移除效果，应自行保存并调用目标的 GameEffectCtrl.TryRemove。

TryEnd 和 TryCancel 只更新 Ability Runtime 状态并从 Active 集合移除，不回滚 Cost、Cooldown 或任何已应用 Effect。Clear 将 Active Runtime 标记为 Cancelled 并清空 Spec，同样不处理外部持有的 GE。

## Editor

GA Editor 内嵌在 GAS_SettingWindow 的 GA 选项卡中，采用 GameplayAbilityWindow、GameplayAbilityEditorView、GameplayAbilityEditorController 和 GameplayAbilityEditorService 的 MVC 组合。

左侧管理 GA 资产：搜索、创建、复制、删除到回收站、Ping 和双击行内重命名。右侧直接使用 SerializedObject 绑定作者数据，不复制 ViewData。

Validation 只检查 GA 自身契约：Cost 非 Instant、Cooldown 为 Instant、SelfEffects 或 TargetEffects 空引用为 Error；没有执行 Effect 为 Info。Tag 选择由 GameplayTag PropertyDrawer 负责，GE 内部问题由 GE Editor 负责。

## 手动测试

GameplayAbilityOdinTester 通过公开 Runtime API 提供初始化、Give、Activate、Self/单目标/多目标 Execute、End、Cancel、外部移除返回 Active GE 和基础生命周期一键测试。

完整业务测试还应准备能够覆盖 Cost、Cooldown、Self、Target、SetByCaller 和目标拒绝的 GA/GE 资产。测试时确认 End/Cancel 后返回的 Active GE 仍由外部管理。
### 资产列表校验提示

Controller 在资产扫描、Undo/Redo、项目变化和当前 GA 字段提交后计算校验状态。存在 Error 的 GA 在左侧 ListView 中显示红色背景；只有 Info 或完全通过时保持普通背景。

ListView 的虚拟化行只读取 Controller 缓存并切换 USS class，不在 bindItem 中执行 Validator。行回收和重新绑定时会清理旧状态，避免滚动后背景色串到其他资产。
