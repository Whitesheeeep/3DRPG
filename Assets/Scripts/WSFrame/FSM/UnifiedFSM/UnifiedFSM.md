# UnifiedFSM 使用与扩展说明

`UnifiedFSM` 是 WSFrame 下的一套统一状态机实现，目标是让同一套状态类既能用于普通 FSM，也能用于 HFSM。

核心思想：

- 普通状态继承 `StateBase<TStateId, TOwner>`。
- 状态机 `StateMachine<TStateId, TOwner>` 本身也是一个状态。
- 因为状态机也是状态，所以一个 `StateMachine` 可以被添加到另一个 `StateMachine` 中，形成 HFSM。
- 支持状态内主动 `ChangeState`。
- 支持 `Transition` 条件自动切换。
- `AnyTransition` 优先于当前状态自己的普通 Transition。
- 自动 Transition 每帧最多触发一次，避免同一帧连续跳多个状态。

命名空间：

```csharp
using WS_Modules.FSM;
```

## 核心类型

### IState

所有状态都实现 `IState<TStateId, TOwner>`。

生命周期：

```csharp
bool CanEnter();
void Init(TOwner owner, IStateMachine<TStateId, TOwner> machine);
void OnEnter();
void OnUpdate();
void OnFixedUpdate();
void OnLateUpdate();
void OnAnimationMove();
void OnExit();
```

说明：

- `CanEnter()` 是目标状态进入条件。
- `OnEnter()` 在进入状态时调用一次。
- `OnUpdate()` 需要外部显式驱动，一般从 MonoBehaviour 的 `Update()` 调用根状态机。
- `OnFixedUpdate()` 一般从 MonoBehaviour 的 `FixedUpdate()` 调用根状态机。
- `OnLateUpdate()` 一般从 MonoBehaviour 的 `LateUpdate()` 调用根状态机。
- `OnAnimationMove()` 由 Unity 的 `OnAnimatorMove()` 回调转发，用于按当前状态处理根运动。- `OnExit()` 在离开状态时调用一次。

### StateBase

类状态基类，推荐正式业务状态继承它。

```csharp
public class IdleState : StateBase<PlayerState, PlayerController>
{
    public IdleState() : base(PlayerState.Idle)
    {
    }

    public override void OnEnter()
    {
        Owner.PlayIdleAnimation();
    }

    public override void OnUpdate()
    {
        if (Owner.MoveInput.sqrMagnitude > 0.01f)
        {
            Machine.ChangeState(PlayerState.Move);
        }
    }
}
```

### CustomState

链式状态，适合简单状态、测试、示例。

```csharp
fsm.State(PlayerState.Idle)
    .OnEnter(state => state.Owner.PlayIdleAnimation())
    .OnUpdate(state =>
    {
        if (state.Owner.MoveInput.sqrMagnitude > 0.01f)
        {
            state.Machine.ChangeState(PlayerState.Move);
        }
    });
```

如果同一个 `StateId` 已经添加了类状态或子状态机，再调用 `State(id)` 会返回 `null`。

### StateMachine

状态机本身继承 `StateBase`，因此可以作为状态加入父状态机。

常用 API：

```csharp
AddState(IState<TStateId, TOwner> state);
SetDefaultState(TStateId stateId);
bool ChangeState(TStateId stateId);
AddTransition(Transition<TStateId, TOwner> transition);
AddAnyTransition(Transition<TStateId, TOwner> transition);
```

`ChangeState` 返回 `false` 的情况：

- 目标状态不存在。
- 目标状态就是当前状态。
- 目标状态 `CanEnter()` 返回 `false`。

## 普通 FSM 示例

```csharp
using UnityEngine;
using WS_Modules.FSM;

public enum PlayerState
{
    Root,
    Idle,
    Move,
    Attack
}

public class PlayerController : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }
    public bool AttackPressed { get; private set; }

    private StateMachine<PlayerState, PlayerController> mFSM;

    private void Awake()
    {
        mFSM = new StateMachine<PlayerState, PlayerController>(PlayerState.Root, this);

        mFSM.AddState(new IdleState());
        mFSM.AddState(new MoveState());
        mFSM.AddState(new AttackState());
        mFSM.SetDefaultState(PlayerState.Idle);

        mFSM.OnEnter();
    }

    private void Update()
    {
        MoveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        AttackPressed = Input.GetMouseButtonDown(0);

        mFSM.OnUpdate();
    }

    private void FixedUpdate()
    {
        mFSM.OnFixedUpdate();
    }
}

public class IdleState : StateBase<PlayerState, PlayerController>
{
    public IdleState() : base(PlayerState.Idle)
    {
    }

    public override void OnUpdate()
    {
        if (Owner.AttackPressed)
        {
            Machine.ChangeState(PlayerState.Attack);
            return;
        }

        if (Owner.MoveInput.sqrMagnitude > 0.01f)
        {
            Machine.ChangeState(PlayerState.Move);
        }
    }
}

public class MoveState : StateBase<PlayerState, PlayerController>
{
    public MoveState() : base(PlayerState.Move)
    {
    }

    public override void OnUpdate()
    {
        if (Owner.AttackPressed)
        {
            Machine.ChangeState(PlayerState.Attack);
            return;
        }

        if (Owner.MoveInput.sqrMagnitude <= 0.01f)
        {
            Machine.ChangeState(PlayerState.Idle);
        }
    }
}

public class AttackState : StateBase<PlayerState, PlayerController>
{
    public AttackState() : base(PlayerState.Attack)
    {
    }

    public override void OnEnter()
    {
        Debug.Log("Enter Attack");
    }
}
```

## Transition 自动切换示例

主动切换适合状态内部直接决定跳转；Transition 适合把切换条件集中到状态机构建阶段。

```csharp
mFSM.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Move)
    .AddCondition(owner => owner.MoveInput.sqrMagnitude > 0.01f));

mFSM.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Move, PlayerState.Idle)
    .AddCondition(owner => owner.MoveInput.sqrMagnitude <= 0.01f));
```

状态机每帧执行 `OnUpdate()` 时，会先检测自动 Transition：

```csharp
private void Update()
{
    mFSM.OnUpdate();
}
```

注意：

- Transition 条件通过后，还会检查目标状态的 `CanEnter()`。
- 自动 Transition 成功后，本帧不会再执行新状态的 `OnUpdate()`。
- 每帧最多自动切换一次。
- 在 HFSM 中，父状态机会先检查自己的 `AnyTransition` 和普通 Transition；如果父级没有发生切换，才会把 `OnUpdate()` 下发给当前子状态，让子状态机检查自己的跳转逻辑。

## AnyTransition 示例

`AnyTransition` 表示从任意状态都可以触发的过渡，适合死亡、受击、打断、强制控制等高优先级状态。

```csharp
mFSM.AddAnyTransition(new Transition<PlayerState, PlayerController>(PlayerState.Root, PlayerState.Attack, 100)
    .AddCondition(owner => owner.AttackPressed));
```

说明：

- `AnyTransition` 注册后会忽略 `FromStateId`。
- `AnyTransition` 优先于当前状态自己的普通 Transition。
- `WeightOrder` 越大，检测越靠前。

## Transition 优先级示例

当多个 Transition 同时满足时，优先级高的先执行。

```csharp
mFSM.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Move, 10)
    .AddCondition(owner => owner.MoveInput.sqrMagnitude > 0.01f));

mFSM.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Attack, 100)
    .AddCondition(owner => owner.AttackPressed));
```

如果 `MoveInput` 和 `AttackPressed` 同时满足，会优先进入 `Attack`。

## HFSM 示例

HFSM 的关键是：子状态机也是一个状态。

例如：

```text
Root
  Idle
  Grounded
    Walk
    Run
  Attack
```

示例代码：

```csharp
public enum PlayerState
{
    Root,
    Idle,
    Grounded,
    Walk,
    Run,
    Attack
}

private StateMachine<PlayerState, PlayerController> BuildRootFSM()
{
    var root = new StateMachine<PlayerState, PlayerController>(PlayerState.Root, this);

    root.AddState(new IdleState());
    root.AddState(BuildGroundedFSM());
    root.AddState(new AttackState());
    root.SetDefaultState(PlayerState.Idle);

    root.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Grounded)
        .AddCondition(owner => owner.MoveInput.sqrMagnitude > 0.01f));

    root.AddAnyTransition(new Transition<PlayerState, PlayerController>(PlayerState.Root, PlayerState.Attack, 100)
        .AddCondition(owner => owner.AttackPressed));

    return root;
}

private StateMachine<PlayerState, PlayerController> BuildGroundedFSM()
{
    var grounded = new StateMachine<PlayerState, PlayerController>(PlayerState.Grounded);

    grounded.AddState(new WalkState());
    grounded.AddState(new RunState());
    grounded.SetDefaultState(PlayerState.Walk);

    grounded.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Walk, PlayerState.Run)
        .AddCondition(owner => owner.MoveInput.magnitude > 0.8f));

    grounded.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Run, PlayerState.Walk)
        .AddCondition(owner => owner.MoveInput.magnitude <= 0.8f));

    return grounded;
}
```

运行行为：

- 根状态机切到 `Grounded` 时，会调用 `Grounded.OnEnter()`。
- `Grounded` 是一个子状态机，因此它会自动进入自己的默认子状态 `Walk`。
- 根状态机从 `Grounded` 切走时，会先退出 `Grounded` 当前子状态，再退出 `Grounded` 本身。
- 每帧更新时，先检查根状态机自己的跳转逻辑；根状态机没有切换时，才会执行当前子状态 `Grounded.OnUpdate()`，然后由 `Grounded` 检查 `Walk/Run` 之间的跳转。
- 因此父状态机的跳转优先级天然高于当前子状态机的内部跳转，适合处理死亡、受击、打断、锁定等全局高优先级状态。

## 自定义 CanEnter 示例

`CanEnter()` 适合做目标状态进入守卫。

```csharp
public class AttackState : StateBase<PlayerState, PlayerController>
{
    public AttackState() : base(PlayerState.Attack)
    {
    }

    public override bool CanEnter()
    {
        return Owner.HasWeapon && Owner.Stamina > 0;
    }

    public override void OnEnter()
    {
        Owner.PlayAttackAnimation();
    }
}
```

即使 Transition 条件满足，只要 `CanEnter()` 返回 `false`，也不会进入该状态。

## 自定义 Condition 示例

简单条件推荐直接用 lambda：

```csharp
new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Attack)
    .AddCondition(owner => owner.AttackPressed);
```

复杂条件可以实现 `ICondition<TOwner>`：

```csharp
public class HasEnoughStaminaCondition : ICondition<PlayerController>
{
    private readonly float mRequiredStamina;

    public HasEnoughStaminaCondition(float requiredStamina)
    {
        mRequiredStamina = requiredStamina;
    }

    public bool Tick(PlayerController owner)
    {
        return owner.Stamina >= mRequiredStamina;
    }
}
```

使用：

```csharp
mFSM.AddTransition(new Transition<PlayerState, PlayerController>(PlayerState.Idle, PlayerState.Attack)
    .AddCondition(new HasEnoughStaminaCondition(20f))
    .AddCondition(owner => owner.AttackPressed));
```

## Debug 状态树输出

`StateMachine` 提供了无参 `ToDebugString()`，可以直接输出当前状态机拥有的状态树：

```csharp
Debug.Log(mFSM.ToDebugString());
```

`ToString()` 会转发到 `ToDebugString()`，所以也可以直接：

```csharp
Debug.Log(mFSM);
```

示例输出：

```text
Root [StateMachine]
├─ Idle [Default]
├─ Grounded [Current, StateMachine]
│  ├─ Walk [Current, Default]
│  └─ Run
└─ Attack
```

输出规则：

- 普通状态默认由 `StateBase.ToDebugString` 输出。
- 子状态机由 `StateMachine.ToDebugString` 递归输出。
- `[Current]` 表示当前状态机的当前子状态。
- `[Default]` 表示当前状态机的默认子状态。
- `[StateMachine]` 表示该节点本身是一个状态机。
- 直接实现 `IState` 但没有继承 `StateBase` 的状态，会使用兜底格式输出状态 id。

特殊状态可以重写 `ToDebugString` 输出额外业务信息：

```csharp
public class AttackState : StateBase<PlayerState, PlayerController>
{
    public AttackState() : base(PlayerState.Attack)
    {
    }

    public override string ToDebugString(string indent, bool isLast, bool isCurrent, bool isDefault)
    {
        return base.ToDebugString(indent, isLast, isCurrent, isDefault)
               + $" Cooldown={Owner.AttackCooldown}";
    }
}
```

## 推荐约定

- 状态 id 推荐使用 enum，不建议在业务代码里大量散落 string。
- 复杂状态使用 `StateBase` 子类，简单测试或临时状态使用 `CustomState`。
- 根状态机由 MonoBehaviour 显式驱动，不默认绑定 `PublicMono`。
- `CanEnter()` 放目标状态自己的进入限制。
- Transition 条件放“从 A 到 B 的触发条件”。
- AnyTransition 只用于高优先级全局打断，避免过度使用导致跳转关系不清晰。
- HFSM 子状态机需要设置自己的默认子状态。

## 常见问题

### 为什么 StateMachine 继承 StateBase？

为了让状态机可以作为父状态机中的一个状态使用。这样普通 FSM 和 HFSM 就能使用同一套调度接口。

### ChangeState 为什么返回 false 而不是抛异常？

状态切换在运行时可能因为条件不满足而失败，返回 false 更适合业务判断。  
配置阶段错误，例如 `SetDefaultState` 指向不存在的状态，会直接抛异常，方便尽早发现问题。

### Transition 和主动 ChangeState 应该怎么选？

状态内部明确知道何时跳转时，用主动 `ChangeState`。  
希望集中维护跳转条件，或做类似 Animator 的条件驱动切换时，用 `Transition`。

二者可以混用。

### 为什么自动 Transition 成功后本帧不执行新状态 OnUpdate？

这样可以避免“刚进入状态就立刻执行一帧逻辑”造成重复消费输入或同帧连跳。  
新状态会从下一帧开始执行 `OnUpdate()`。
