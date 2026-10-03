# 通用奖励系统架构

> 文档状态：货币、可堆叠物品、武器和圣遗物奖励已接入
> 范围：奖励定义、跨 Manager 的准备提交流程、任务领奖接入与扩展方式

## 1. 职责和数据流

RewardSystem 解释静态奖励定义并编排一次发放。玩家余额和库存仍分别由 CurrencyManager、StackableInventoryManager、WeaponInventoryManager 与 ArtifactInventoryManager 持有。奖励系统不保存领取状态，也不注册存档模块。

~~~mermaid
flowchart LR
    Definition[RewardDefinition 配置] --> System[RewardSystem]
    System --> Registry[RewardHandlerRegistry]
    Registry --> CurrencyHandler[CurrencyRewardHandler]
    Registry --> ItemHandler[ItemRewardHandler]
    CurrencyHandler --> Currency[CurrencyManager]
    ItemHandler --> ItemManager[ItemManager 查询定义类型]
    ItemHandler --> Stackable[StackableInventoryManager]
    ItemHandler --> Weapon[WeaponInventoryManager]
    ItemHandler --> Artifact[ArtifactInventoryManager]
    Task[TaskSystem] -->|准备奖励与完成事实| System
~~~

## 2. 静态奖励模型

| 类型 | 配置字段 | 运行时解释 |
| --- | --- | --- |
| `RewardDefinition` | 多态奖励基类 | 由 `SerializeReference` 放入业务配置；自身不保存领取状态 |
| `CurrencyRewardDefinition` | 一个或多个 `CurrencyId + Amount` | 合并重复货币，检查钱包余额上限后批量增加 |
| `ItemRewardDefinition` | 一个或多个 `ItemId + Quantity` | 按 ItemDefinition 类型路由到可堆叠背包、武器或圣遗物库存 |

`CurrencyId.Mola` 是当前金币（摩拉）入口；`CurrencyId.YuanShi` 作为第二种现有货币继续支持。武器和圣遗物按数量创建独立、未装备的新实例，沿用库存 Manager 的默认初始状态、获得序号、发现状态和 New 提示。

相同类型的多条奖励配置会在预检前合并。数量合并溢出、空定义、无效 ID 和未知 Handler 属于配置错误，立即明确报错。钱包上限、可堆叠物品上限和装备容量不足是业务拒绝，返回结构化 `RewardGrantResult`。

## 3. 准备、提交和通知

~~~mermaid
sequenceDiagram
    participant Caller as 普通调用方或 TaskSystem
    participant Reward as RewardSystem
    participant Handler as Reward Handler
    participant Manager as 钱包与库存 Manager
    participant Task as TaskSystem
    participant Observer as 事件订阅者

    Caller->>Reward: 准备奖励定义
    Reward->>Handler: 按精确类型解析并合并
    Handler->>Manager: 无副作用准备领域批次
    Manager-->>Handler: 结构化校验结果与待提交状态
    Handler-->>Reward: 全部批次
    alt 任一领域拒绝
        Reward-->>Caller: 拒绝结果，不写入玩家数据
    else 准备成功
        Reward->>Reward: 检查所有批次仍可提交
        Reward->>Manager: 依次写入全部领域状态
        opt 任务领奖
            Reward->>Task: 记录完成 ID 并清理活动、追踪和未读状态
            Task->>Observer: 发布任务完成通知
        end
        Reward->>Observer: 发布货币、库存与装备变化通知
    end
~~~

准备批次只在同一同步调用中使用；提交前重新检查依赖的余额、数量、容量及获得序号快照。全部 Manager 提交前不会执行领域事件回调。红点自身值在状态提交阶段更新，红点树在其既有帧末流程统一重算。

任务接入时，TaskSystem 先确认 `Claimable`，准备并提交奖励，再写入已完成 ID、清除追踪和未读事实，最后发布奖励通知。任务领奖重入保护持续覆盖完整流程。完成事实和奖励已经提交后，订阅者异常只记录日志，不回滚数据或重新开放领奖。

`CanGrant` 只准备并丢弃批次，不改变玩家数据；`TryGrant` 每次自行重新准备，因此调用方不需要先调用 `CanGrant`。空列表是成功的空操作。

## 4. 注册与代码位置

~~~
Assets/Scripts/RewardSystem/
├─ Runtime/Definitions/   RewardDefinition、货币和物品多态配置
├─ Runtime/Interfaces/    IRewardHandler、IPreparedRewardPart
├─ Runtime/Handlers/      货币与物品解释器
├─ Runtime/Registries/    精确类型 Handler 注册表
├─ Runtime/BuisnessOperationResult/  RewardGrantResult
├─ Runtime/Transactions/  本次发放的跨领域批次
├─ Runtime/Core/           RewardSystem
├─ Test/                  Odin Inspector 手动验证入口
└─ RewardSystem_Architecture.md
~~~

当前奖励 Handler 没有泛型基类，均实现 [IRewardHandler](Runtime/Interfaces/IRewardHandler.cs)，类型使用 `RPG.RewardSystemNS` 命名空间。新增奖励类型时，在 `Runtime/Definitions/` 新建继承 [RewardDefinition](Runtime/Definitions/RewardDefinition.cs) 的配置类型，在 `Runtime/Handlers/` 新建对应 Handler，并把 Handler 实例加入 [GameArchitecture](../Game/Runtime/Architecture/GameArchitecture.cs) 对 [RewardHandlerRegistry](Runtime/Registries/RewardHandlerRegistry.cs) 的 `RegisterDefault(...)` 参数列表。现有默认项是 `CurrencyRewardHandler` 和 `ItemRewardHandler`。`GameArchitecture` 创建全局默认 Registry 和 RewardSystem；默认 Handler 实例在此处装配，不由 RewardSystem 扫描发现。注册表按精确 CLR 类型解析，不做反射扫描。

最小奖励配置示例：

~~~csharp
using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>配置一次示例代币奖励。</summary>
[Serializable]
public sealed class ExampleTokenRewardDefinition : RewardDefinition
{
    [SerializeField, MinValue(1)] private int amount = 1;

    /// <summary>获取发放数量。</summary>
    public int Amount => amount;

    /// <summary>确保配置的代币数量为正数。</summary>
    /// <exception cref="ArgumentOutOfRangeException">奖励数量不是正数时抛出。</exception>
    public override void Validate()
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "奖励数量必须大于零。");
    }
}
~~~

对应 Handler 实现 `IRewardHandler.DefinitionType` 和 `TryPrepare()`。RewardSystem 会把所有同一精确类型的定义一起交给该 Handler；Handler 负责合并重复 ID、按该定义的数量规则检查溢出，并调用玩家数据所属 Manager 的无副作用准备入口。业务容量或上限不足时，返回 `false`、空批次和带失败领域的 `RewardGrantResult`；定义非法或依赖 Handler 未登记时明确报错。

Handler 的接口骨架如下，标记为“待实现”的部分要按具体领域 Manager 的 API 完成；不能保留占位异常：

~~~csharp
using System;
using System.Collections.Generic;

/// <summary>将示例代币定义准备为领域奖励批次。</summary>
public sealed class ExampleTokenRewardHandler : IRewardHandler
{
    /// <summary>获取该 Handler 精确支持的奖励定义类型。</summary>
    public Type DefinitionType => typeof(ExampleTokenRewardDefinition);

    /// <summary>合并示例奖励并无副作用地准备 Manager 写入。</summary>
    /// <param name="definitions">本次所有同类型奖励定义。</param>
    /// <param name="preparedParts">成功时返回的提交批次。</param>
    /// <param name="result">成功或业务拒绝结果。</param>
    /// <returns>全部准备成功时返回 true。</returns>
    /// <exception cref="NotImplementedException">此接口骨架尚未接入领域 Manager 时抛出。</exception>
    public bool TryPrepare(
        IReadOnlyList<RewardDefinition> definitions,
        out IReadOnlyList<IPreparedRewardPart> preparedParts,
        out RewardGrantResult result)
    {
        // 待实现：合并定义，调用领域 Manager 预检，并按业务结果填充三个输出。
        throw new NotImplementedException("扩展示例骨架，需实现领域准备逻辑。");
    }
}
~~~

这是 Handler 入口骨架，不是可直接运行的实现。实际成功路径见下方批次适配示例；容量不足等业务拒绝应设置 `preparedParts` 为空、提供准确 `result` 并返回 `false`，不能抛配置异常。

当前 `CurrencyManager` 和三个库存 Manager 的准备 API 返回三个委托：状态有效性检查、写入玩家状态、发布变化通知。Handler 通常直接把它们交给 `PreparedRewardGrantPart`，由适配器实现 `IPreparedRewardPart`；一般扩展不需要自己实现该接口。下面是 `CurrencyRewardHandler` 使用货币 Manager 输出的成功路径结构片段：

~~~csharp
if (!currencyManager.TryPrepareRewardChanges(
        changes,
        out CurrencyOperationResult walletResult,
        out Func<bool> canCommit,
        out Action commitState,
        out Action publishNotifications))
{
    preparedParts = Array.Empty<IPreparedRewardPart>();
    result = ConvertWalletResult(walletResult);
    return false;
}

preparedParts = new IPreparedRewardPart[]
{
    new PreparedRewardGrantPart(canCommit, commitState, publishNotifications)
};
result = RewardGrantResult.Success();
return true;
~~~

代码展示货币 Handler 的准备成功路径；`ConvertWalletResult` 表示把钱包的业务失败状态映射为 `RewardGrantResult`。`PreparedRewardGrantPart` 把三个委托封装为命令批次，供统一流程依次检查、写入及通知。当前它是 `internal`，奖励 Handler 位于项目默认 `Assembly-CSharp` 时可以直接使用；若未来把 Handler 放进独立 `.asmdef`，该类型不可见，需要由奖励模块提供可访问的批次工厂，或在确有特殊提交需求时自行实现公开契约 [IPreparedRewardPart](Runtime/Interfaces/IPreparedRewardPart.cs)。

把三个委托收拢为领域专属的准备批次对象，可以作为未来的可选 API 整理；当前 Manager 与 Handler 仍按上面的三个委托契约协作。

默认注册在 `GameArchitecture` 的 Handler 实例装配处完成：

~~~csharp
rewardHandlerRegistry.RegisterDefault(
    new CurrencyRewardHandler(CurrencyManager.Instance),
    new ItemRewardHandler(stackableInventoryManager, weaponInventoryManager, artifactInventoryManager),
    exampleTokenRewardHandler);
~~~

先由架构装配代码创建 `exampleTokenRewardHandler` 并注入它依赖的领域 Manager，再传给 `RegisterDefault(...)`。该方法接收创建好的 Handler 实例，重复调用同一 Registry 不会重复登记；同一定义类型手动重复登记会报错。如果独立装配另一个 Registry，可在创建 RewardSystem 前调用 `Register<TDefinition>(handler)` 登记对应 Handler。Handler 的定义类型声明必须与注册的 `TDefinition` 完全一致。

配置通过业务资产中的 `[SerializeReference]` 奖励列表使用，例如任务的 [TaskDefinition](../TaskSystem/Runtime/Config/TaskDefinition.cs)。已经支持的可堆叠物品、武器和圣遗物通常复用 [ItemRewardDefinition](Runtime/Definitions/ItemRewardDefinition.cs)，不需要按物品类别再建奖励 Handler；已有货币则复用 [CurrencyRewardDefinition](Runtime/Definitions/CurrencyRewardDefinition.cs)。只有奖励数据形状或发放领域不同，才新增定义与 Handler。

如果新奖励要写入一个新的玩家数据领域，先让该领域 Manager 提供准备、提交状态、通知三个阶段：准备期间不能改余额、库存、发现记录、New 提示、红点或获得序号；所有领域准备成功后才提交；全部状态写入后才允许执行事件回调。若新领域的业务拒绝无法由当前 `RewardGrantResult` 准确表达，也要扩展其结果字段，再接入该 Manager。不要让 RewardSystem 直接访问其他 Manager 的内部字典。

普通调用方使用公开 `CanGrant(rewards)` 查询或 `TryGrant(rewards)` 发放，并由调用业务自己管理“是否有资格领取”和防止重复领取。需要在奖励通知之前一并提交业务完成事实的流程，应采用任务系统相同的准备—统一提交—业务事实—发布通知顺序；当前 `TryPrepareGrant()` 是内部入口，任务系统在同一程序集内使用。不要先调用公开 `TryGrant()` 再补写业务完成事实，因为 `TryGrant()` 会在返回前发布奖励通知。

## 5. 扩展关系

~~~mermaid
flowchart TD
    Definition[自定义 RewardDefinition] -->|SerializeReference| Business[业务配置，例如 TaskDefinition]
    Business --> RewardSystem[RewardSystem]
    RewardSystem -->|按精确 CLR 类型| Registry[RewardHandlerRegistry]
    Registry --> Handler[自定义 IRewardHandler]
    Handler -->|无副作用准备| Manager[领域 Manager]
    Manager --> Prepared[PreparedRewardGrantPart]
    Prepared -->|检查与写入| RewardSystem
    RewardSystem -->|全部写入后| Fact[调用方业务完成事实]
    Fact --> Notifications[发布领域通知]
~~~

默认奖励的执行顺序是：准备全部 Handler 批次、检查所有准备状态、连续提交所有 Manager 状态、提交调用方需要的完成事实，然后发布通知。任务系统在状态写入之后先提交完成集合并发送任务事件，最后发布奖励领域通知。一般 `TryGrant()` 没有额外的业务事实步骤，直接在其内部提交并通知。`RewardSystem` 的实现见 [RewardSystem.cs](Runtime/Core/RewardSystem.cs)，提交批次类型见 [PreparedRewardGrant.cs](Runtime/Transactions/PreparedRewardGrant.cs)。

新增奖励 Handler 只需实现 `IRewardHandler` 并按以上注册方式装配一次；“某个业务额外 Handler”不是第二种 Handler 类型。差别仅在于它由全局默认 Registry 的 `RegisterDefault(...)` 装配，还是由一个独立 Registry 通过 `Register<TDefinition>()` 显式登记。

## 6. 边界

- 奖励定义是静态配置；已经领取与否由业务调用方管理。
- RewardSystem 不拥有货币、背包、装备数据，也不保存奖励快照。
- 业务拒绝保证整包零到账；本地同步内存提交不提供进程崩溃或跨存储介质事务保证。
- `RewardSystemOdinTester` 可预检或真实发放 Inspector 配置的奖励。真实发放会修改当前 Play Mode 运行数据，测试时使用专门的测试配置。
