# Marker 系统使用指南

## 1. 职责与边界

Marker 系统把“任意实例层级中的某个 Transform”转换为稳定的语义查询：

```text
TransformMarker
→ MarkerCollection.TryRebuild(ownerRoot)
→ IMarkerProvider.TryGetMarker(MarkerKey)
→ VFX / AttackDetection 等运行时调用方取得 Transform
```

- `MarkerKey` 是可复用的 ScriptableObject 资产，例如 `RightHand`、`WeaponRoot`、`WeaponTip`。
- `TransformMarker` 只声明当前节点对应哪个 Key，不主动查找所属实例或注册自身。
- `MarkerCollection` 由需要查询挂点的实例持有；实例初始化或子层级变化后由所有者显式重建。
- `VfxSkillClipConfig` 保存 `MarkerKey` 资产引用，使每个 VFX Clip 可以声明自己的语义挂点。
- AttackDetection Config 仍不保存 MarkerKey；角色、武器或刀刃检测基准继续由运行时调用上下文传入。
- Config 不保存 Transform 层级路径或场景对象引用。

## 2. 创建并配置 Marker

1. 在 Project 窗口使用 `Create/RPG/Markers/Marker Key` 创建 `MarkerKey` 资产。
2. 在角色、武器、机关等实例 Prefab 的实际挂点节点添加 `TransformMarker`。
3. 把对应 `MarkerKey` 拖入组件的 Key 字段。
4. Marker 的目标就是组件所在 GameObject 的 `transform`，不需要额外填写路径或 Target。

一个实例根节点范围内，同一个 `MarkerKey` 只能出现一次。未配置 Key 或重复 Key 会让整次重建失败，防止技能静默绑定到错误节点。

## 3. 实例持有与重建

实例所有者自行持有 `MarkerCollection`，不要让 Marker 组件依赖具体 Player、NPC、武器或 Skill 类型：

```csharp
using RPG.Markers;
using UnityEngine;

public sealed class MarkerOwnerExample : MonoBehaviour
{
    private readonly MarkerCollection markers = new();

    public IMarkerProvider Markers => markers;

    private void Awake()
    {
        RebuildMarkers();
    }

    public void OnHierarchyChanged()
    {
        RebuildMarkers();
    }

    private void RebuildMarkers()
    {
        if (!markers.TryRebuild(transform, out string error))
            Debug.LogError(error, this);
    }
}
```

`TryRebuild`会扫描未激活子节点，并先在临时字典中完成全部检查。失败时保留上一份有效索引，因此层级重建失败不会破坏实例原本可用的 Marker。

子对象挂入或移除后必须由实例所有者明确再次调用 `TryRebuild`。系统不会监听每一次 Transform 层级变化，也不会在技能执行时重复扫描实例。

## 4. 运行时查询

运行时 VFX 执行器从 Clip 读取 MarkerKey，再向角色 MarkerProvider 查询绑定 Transform。空 MarkerKey 明确使用角色根节点：

```csharp
Transform binding = actor.transform;
if (vfxClip.MarkerKey != null &&
    !actor.Markers.TryGetMarker(vfxClip.MarkerKey, out binding))
{
    // 当前角色或装备没有提供该挂点，本次 VFX Clip 不执行。
    return;
}

vfxExecutor.Play(vfxClip, binding);
```

推荐的数据流为：

```text
VfxSkillClipConfig.MarkerKey
→ IMarkerProvider.TryGetMarker(...)
→ 得到 binding Transform
→ VFX Executor
```

VFX 调度层负责按 `VfxSkillClipConfig.MarkerKey` 解析 Transform；具体粒子执行器只接收已经解析好的 Transform，不反向搜索角色层级。

## 5. 时间轴编辑器预览

选中 VFX Clip 后，其 Inspector 在 Prefab 下方提供“挂点”ObjectField：

- 每个 VFX Clip 可以直接拖入不同的 `MarkerKey` 资产。
- 空值明确表示使用预览角色根节点。
- 选择了 Key，但预览角色副本中找不到对应 `TransformMarker` 时，只跳过该 VFX Clip 并在状态栏报错；其他 VFX、Animation 和 Audio 继续预览。
- Preview Actor 是隔离副本，MarkerCollection 在副本创建时收集一次；切换演示角色后会重新建立索引。

VFX 两种跟随模式的预览语义：

- `FollowBinding`：每帧使用当前动画姿势下的 Marker Transform。
- `KeepWorldPosition`：采样 Clip 起始帧的动画、Root Motion 与 Marker 世界矩阵并冻结，任意顺序跳帧结果一致。

## 6. VFX 场景 Transform 编辑

选中 VFX Clip 后，Inspector 提供：

- `在场景中编辑`：暂停播放并创建可在 Scene View 中操作的独立代理。
- `应用预览 Transform`：把代理世界 Transform 转换到冻结 Marker 空间，通过 Document 写回局部位置、旋转和缩放，并产生一条 Undo。
- `取消场景编辑`：销毁代理并丢弃草稿。

普通隐藏预览实例与编辑代理相互独立：前者继续表达 Config 的确定性结果，后者只保存尚未提交的用户 Transform 草稿。播放、Scrub、切换选择、Config、演示角色或当前 Clip MarkerKey 变化时会销毁未应用代理。

编辑代理使用不可保存对象，不依赖 Hierarchy 作为重新选择入口。失去 Scene 选择后，在 VFX Clip Inspector 点击“选择编辑代理”即可重新选中代理根对象并在 Scene View 中定位；只有代理根 Transform 的位置、旋转或缩放发生有效变化后，“应用预览 Transform”才会写回 Clip 配置。

编辑代理内部使用不含 `ParticleSystem` 的空 Transform 根，Prefab 粒子克隆作为归一化子节点。点击“在场景中编辑”时，子节点按当前播放头绝对时间完成 `Simulate` 后暂停；重新选择的始终是空根，因此 Unity 粒子 Inspector 不会让定帧画面继续播放。

代理只复制 Transform；ParticleSystem 和业务 MonoBehaviour 参数不会写回 Config。所有代理使用 `DontSaveInEditor`，关闭窗口后不会进入预览场景。

## 7. 常见失败

- `角色根节点为空`：角色尚未完成创建，不应调用重建。
- `TransformMarker 没有配置 MarkerKey`：补齐组件的 Key。
- `存在重复 MarkerKey`：保留唯一语义节点，或为另一用途创建不同 Key。
- `预览角色中不存在 MarkerKey`：确认时间轴使用的 Preview Actor 与运行时角色 Prefab 都包含对应 Marker。
- `换装后查询不到武器 Marker`：装备挂入角色层级后再次调用 `TryRebuild`。