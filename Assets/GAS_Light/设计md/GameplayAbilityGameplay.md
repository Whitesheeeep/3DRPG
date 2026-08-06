# Gameplay Ability 设计

## 1. 分层与依赖

Gameplay Ability 按执行生命周期分成同步和异步两条多态分支：

    GameplayAbilityData
    ├─ SynchronousGameplayAbilityData
    └─ AsynchronousGameplayAbilityData

    GameplayAbilityRuntime
    ├─ SynchronousGameplayAbilityRuntime
    └─ AsynchronousGameplayAbilityRuntime

核心 AbilitySystem 不依赖 SkillConfig、碰撞、投射物、动画或具体目标系统。业务模块可以用清晰的业务类名继承对应分支，例如 ProjectileGameplayAbilityData 继承同步基类，CastGameplayAbilityData 继承异步基类。

Runtime 直接持有 Root Task，不增加 Runner、ExecutionNode、通用 Context 或目标黑板。

## 2. 公共 Data 与 Spec

GameplayAbilityData 只保存所有技能共用的作者配置：

- Description
- ActivationTagQuery
- CostEffect
- CooldownEffect

SelfEffects 与 TargetEffects 已从公共 Data 删除。具体技能需要的 GE、目标或投射物配置由具体同步 Data 或具体 Task 保存。

GameplayAbilitySpec 是 ASC 中长期存在的授予状态，保存 Handle、Data 和当前 Level。Runtime 激活时复制 Level 与 SetByCaller，之后修改 Spec Level 不影响已创建 Runtime。

## 3. 激活事务与事件顺序

Controller 激活顺序固定为：

    查找 Spec
    → 校验 Level、Tag、Cost/Cooldown Policy
    → 校验具体 Data 契约
    → 异步 Data 检查 Root Task
    → 创建 Created Runtime 候选
    → 提交 Cooldown
    → 提交 Cost
    → 注册 Runtime 并进入 Active
    → AbilityActivated
    → Runtime.Start

Cost 失败时精确移除本次刚创建的 Cooldown Runtime。Root Task 为空或 Task 配置非法时，必须在 Cost/Cooldown 副作用前拒绝；异步 Ability 由 Owner 主动调用 ASC.Tick 推进。

Runtime 进入终态后，Controller 先将它从 ActiveRuntimes 移除，再发送 AbilityEnded 或 AbilityCancelled。同步 Runtime 可能在 TryActivate 返回前结束，但事件顺序仍是 Activated → Ended。

## 4. 同步 Ability

SynchronousGameplayAbilityData 直接覆盖 Execute。执行返回后 Runtime 自动 Complete 并进入 Ended，不创建 Task，也不注册 Tick 回调。

同步只表示 Ability 业务入口在当前调用内完成，不代表它产生的对象必须立即销毁。例如投射物技能可以同步创建并发射投射物，投射物随后独立管理碰撞和生命周期。

同步实现不得注册持续更新或等待外部事件；需要等待时应改为异步 Ability。同步、Passive 和 Projectile 三类基础技能：

- `InstantGameplayAbilityData` 保存多个 Instant GE。激活时按列表顺序逐个提交；某一项 GE 被目标拒绝只记录失败，不回滚已成功项，也不阻止后续项。
- `PassiveGameplayAbilityData` 使用不主动完成的 `PassiveGameplayAbilityTaskConfig` 保持 Runtime Active。每次成功应用的 Infinite GE Runtime 都保存为本次 Runtime 的精确句柄；End/Cancel 时只移除这些句柄，不使用按 Source 的全量删除，因此不会误删其他技能或外部效果。Passive GE 当前要求 `StackingType.None`，保证每次激活拥有独立句柄。
- `ProjectileGameplayAbilityData` 在同步入口中创建投射物，GA Runtime 随即 Ended。投射物对象自行管理移动和寿命，不占用 GA Task，也不由 Ability 保存 Target。Sphere 测试 SO 使用 Odin Tester 提供的 Transform 作为出生点，仅验证“创建后独立存活”；碰撞、范围、阵营和命中 GE 留给后续业务类。

上述配置均可作为 ScriptableObject 在 GA Editor 中创建和检查。Odin Tester 使用 SO 引用而不是每次按钮点击临时创建 Ability Data，便于检查多态字段和 Validation。 测试夹具位于 Assets/GAS_Light/AbilitySystem/Runtime/GAData/SO/，可直接拖到 GameplayAbilityOdinTester 的 Skill 测试 SO 字段。
GA Odin 测试器还需要配置 `Assets/GAS_Light/AttributeSystem/Editor/TestAssets/GameplayAttributeTestSet.asset`。每次重置测试时，Tester 会将该 Set 导入新的 ASC；否则依赖 Attribute 的 Cost GE 无法提交。该 Set 只用于测试夹具，不是 GA Runtime 的必需依赖。

`GA_Test_PassiveSkill.asset` 当前 `Effects` 为空，因此只能验证 Passive Runtime 的激活、保持和结束生命周期；`AppliedEffects=0` 是当前资产配置结果，不代表 Runtime 错误。

## 5. 异步 Ability 与 Task

AsynchronousGameplayAbilityData 使用 SerializeReference 保存 Root Task Config。每次激活都从 Config 创建全新的 GameplayAbilityTask 实例，多个 Runtime 之间不共享运行状态。
`Assets/GAS_Light/AbilitySystem/Runtime/GAData/SO/GA_Test_AsyncAbility.asset` 是基础异步 Ability 测试资产，使用一秒 `WaitDurationGameplayAbilityTaskConfig`。激活后 Runtime 保持 Active，Owner 调用 `ASC.Tick` 累计一秒后 Root Task 完成，Runtime 进入 Ended。

Root Task 生命周期驱动 Runtime：

    Root Completed → Runtime Ended
    外部 TryEnd    → Root Stopped  → Runtime Ended
    TryCancel      → Root Cancelled → Runtime Cancelled
    Clear          → 逐个 Cancel

Task 不能直接写 Runtime.State。Task 完成只通知父 Sequence 或 Root Runtime。

GameplayAbilityTaskState：

- Inactive
- Running
- Completed
- Stopped
- Cancelled

## 6. Sequence 与 WaitDuration

Sequence 按顺序启动子 Task：

- 子 Task 同步完成时在当前调用内继续下一项。
- 子 Task 保持 Running 时等待完成通知。
- 空 Sequence 合法并立即完成。
- 空子项非法，Editor 和 Runtime 激活校验都会拒绝。
- Stop/Cancel 只传播给当前运行子项。
- 推进标记用于处理 Start 内同步完成回调，避免递归重复推进。

GameplayAbilityTickTask：

- 启动时向所属 ASC 的 AbilityCtrl 注册 Tick 回调。
- AbilityCtrl 返回 WSFrame 的 IUnRegister；Tick Task 持有该句柄，并在 Stop、Cancel 或 Complete 时调用 UnRegister。
- IUnRegister 只负责 Task 从 AbilityCtrl 注销 Tick 回调，不改变 Runtime、ASC 或 GameObject 的生命周期。
- 每次更新调用业务子类的 OnTick(deltaTime)。
- 业务 Task 在 OnTick 中驱动动画进度、蓄力值、位移或持续输入，并在完成时调用 Complete()。
- Stop、Cancel 和 Complete 都会在完成通知前释放 Tick 注册。
- Tick Task 不保存 Animator、AnimationClip、SkillConfig、Transform 或目标信息。

WaitDuration：

- WaitDuration 继承 GameplayAbilityTickTask。
- Duration 必须为有限值且不小于 0。
- 0 秒在完成注册后立即完成。
- 正数通过 Tick 累计时间，达到时长后完成。
- Stop 与 Cancel 都释放注册，注册只释放一次。

Odin Tester 中的 Tick Probe Task 按 ASC Tick 次数完成，用于验证“持续若干帧执行内容、达到条件后通知完成”的生命周期。未来动画 Task 可以直接继承 GameplayAbilityTickTask，不需要修改 Ability Runtime。

同步 Ability 不使用 Tick 注册；异步 Ability 由所属 ASC 的 AbilityCtrl 接收统一 Tick。

## 7. 统一 Tick 入口

外部 Owner 负责主动调用 `AbilitySystemComponentBase.Tick(deltaTime)`，GAS 不自动绑定 Unity `Update`。ASC 内部固定先调用 `GameEffectCtrl.Tick(deltaTime)`，再调用 `GameplayAbilityCtrl.Tick(deltaTime)`，因此同一帧先推进 GE 的周期与到期，再推进 GA 的 Tick Task。`deltaTime` 为负数、NaN 或 Infinity 时会被安全忽略。

同步 Ability 不使用 Tick 注册；异步 Ability 的 WaitDuration、动画、蓄力和其他持续 Task 都由所属 ASC 的 AbilityCtrl 接收 Tick。Task 持有的 `IUnRegister` 只管理这一个回调的注销，不承担外部对象或 ASC 的生命周期。
## 8. Editor

GA Editor 通过 TypeCache 发现公开、非抽象、非泛型的 GameplayAbilityData 子类。抽象同步/异步基类不会出现在 Create 菜单；内部测试类型也不会出现。

公共字段由固定 UXML 绑定，具体子类字段通过 SerializedObject 动态生成 PropertyField，不复制 Model 或 ViewData。

Task Config PropertyDrawer 支持：

- 发现具体 Task Config 类型。
- 创建、替换与清空 managed reference。
- Sequence 子项复用同一 Drawer。
- 使用 Unity Undo 写回。

Validation 检查：

- Cost 必须是 Instant。
- Cooldown 必须是 Duration 或 Infinite。
- 异步 Root Task 不能为空。
- Sequence 子项不能为空。
- Wait Duration 不能为负数、NaN 或 Infinity。

存在 Error 的 GA 在资产 ListView 中显示错误背景。

## 9. 当前边界

本阶段不实现：

- Parallel Task
- CancelReason
- SkillConfig Task
- Target Context
- 碰撞、范围和阵营筛选
- 投射物业务 Task
- GE 业务 Task
- 具体动画系统、输入、网络预测

这些能力后续通过具体同步 Data、具体异步 Data 或业务 Task 扩展，不改变当前同步/异步基类和 Runtime 生命周期。

## 10. Root Task 完成语义

当前 Root Task 代表整套 Ability 流程。Root Task 完成后，GameplayAbilityRuntime 自动进入 Ended；如果需要多个阶段，使用 Sequence 继续组织子 Task。

这是一种当前阶段的简化约定。未来若需要 UE 风格的“Task 完成但 Ability 仍保持 Active”，应增加显式 Ability 流程或 Completion Policy，由 Ability 决定何时 TryEnd，而不是让 Task 私自修改 Runtime 状态。
