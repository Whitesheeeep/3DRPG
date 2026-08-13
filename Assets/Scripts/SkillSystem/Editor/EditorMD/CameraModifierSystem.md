# Camera Modifier System

Camera Modifier V1 只修改 Gameplay Brain 的最终输出，不负责切换镜头。

持续 Shake 与单次 Impulse 的运行时 Manager、Profile 和测试方法见：

```text
Assets/Scripts/CameraSystem/CameraShakeSystem.md
```

当前时间轴中的 `ShakeCameraModifierData`仍使用旧的内嵌参数；在后续 Profile 接入完成前，不要让同一表现同时调用 Profile Shake，避免重复叠加。

```mermaid
flowchart LR
    Config["CameraModifierTrackConfig"] --> Player["CameraModifierTimelinePlayer"]
    Player --> Manager["CinemachineManager"]
    Brain["CinemachineBrain"] --> Camera["Main Camera"]
    Manager --> Camera
```

## 使用方法

1. 在技能时间轴轨道面板右键添加“摄像机修饰轨道”。
2. 使用轨道“+”添加 Modifier Clip，在 Inspector 选择 FOV 或 Shake。
3. FOV 的 Scale 是运行时权威值；Value 仅由工具栏 Gameplay 镜头 Prefab 的参考 FOV 换算。
4. 在场景中的 CameraSystemRoot 挂载 `CinemachineManager`，配置 Gameplay `CinemachineBrain`。
5. 技能执行实例创建 `CameraModifierTimelinePlayer`，逐帧调用 `SampleFrame`，结束时调用 `Dispose`。

后创建的 Player 位于更高层。Exclusive 会屏蔽该通道中更早创建的请求，之后创建的 Additive 仍可叠加。
