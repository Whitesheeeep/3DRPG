#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Animancer;
using RPG.Character.Animation;
using RPG.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WS_Modules.UIModule;

namespace RPG.DialogueSystemModule.Editor.Tests
{
    /// <summary>创建内存对话图、参与者和真实窗口引用，供 Editor 集成测试复用。</summary>
    internal sealed class DialogueEditorTestFixture : IDisposable
    {
        #region 状态与依赖

        // 测试只持有运行时注入的窗口和系统，不改变生产架构的注册关系。
        private readonly DialogueWindow window;
        private readonly List<UnityEngine.Object> temporaryObjects = new();

        /// <summary>创建绑定到当前预加载窗口的测试夹具。</summary>
        /// <param name="dialogueWindow">由 UIManager 预加载的真实窗口。</param>
        public DialogueEditorTestFixture(DialogueWindow dialogueWindow)
        {
            window = dialogueWindow ?? throw new ArgumentNullException(nameof(dialogueWindow));
            System = RPG.Game.GameArchitecture.Interface.GetSystem<DialogueSystem>();
            Data = window.GameObject.GetComponent<DialogueWindowDataComponent>() ??
                throw new InvalidOperationException("DialogueWindow 缺少 DialogueWindowDataComponent。");
            if (Data.SpeakContentTypeWriter == null)
                throw new InvalidOperationException("DialogueWindowDataComponent 未绑定 SpeakContentTypeWriter。");
        }

        /// <summary>获取真实 DialogueWindow。</summary>
        public DialogueWindow Window => window;

        /// <summary>获取真实 DialogueSystem。</summary>
        public DialogueSystem System { get; }

        /// <summary>获取窗口生成绑定数据。</summary>
        public DialogueWindowDataComponent Data { get; }

        /// <summary>获取当前正文使用的 TypeWriter。</summary>
        public TMProTypeWriter TypeWriter => Data.SpeakContentTypeWriter;

        /// <summary>获取对白 View 是否仍在等待 TypeWriter 的异步完成回调。</summary>
        public bool IsSpeechRevealing
        {
            get
            {
                DialogueUIController controller = GetField<DialogueUIController>(window, "controller");
                DialogueSpeechView speechView = GetField<DialogueSpeechView>(controller, "speechView");
                return speechView.IsRevealing;
            }
        }

        /// <summary>获取当前 Choice 区域根节点。</summary>
        public Transform ChoiceRoot => Data.DialogueChoiceRootTransform;

        /// <summary>创建仅存在于内存中的 Speaker 身份资产。</summary>
        /// <param name="name">资产显示名称。</param>
        /// <returns>临时 Speaker。</returns>
        public DialogueSpeaker CreateSpeaker(string name)
        {
            DialogueSpeaker speaker = Track(ScriptableObject.CreateInstance<DialogueSpeaker>());
            speaker.name = name;
            return speaker;
        }

        #endregion

        #region 图资产构造

        /// <summary>创建一张指定直线文本的内存对话图。</summary>
        /// <param name="speaker">首句 Speaker。</param>
        /// <param name="text">首句文本。</param>
        /// <param name="secondText">后续文本；为空时首句直接结束。</param>
        /// <returns>未写入 AssetDatabase 的临时 DialogueAsset。</returns>
        public DialogueAsset CreateLinear(DialogueSpeaker speaker, string text, string secondText = null)
        {
            DialogueAsset asset = Track(ScriptableObject.CreateInstance<DialogueAsset>());
            DialogueEntryNode entry = Track(ScriptableObject.CreateInstance<DialogueEntryNode>());
            DialogueSpeechNode first = CreateSpeech(speaker, text);
            DialogueNode target = null;
            if (!string.IsNullOrEmpty(secondText)) target = CreateSpeech(speaker, secondText);
            else target = CreateEnd();
            first.SetNextNode(target);
            asset.SetEntryNode(entry);
            asset.AddNode(first);
            asset.AddNode(target);
            entry.SetFirstSpeechNode(first);
            asset.EnsureStableIds();
            return asset;
        }

        /// <summary>创建一张带有两个选项和可选条件的内存对话图。</summary>
        /// <param name="speaker">对白 Speaker。</param>
        /// <param name="firstCondition">首个选项条件；为空表示无条件。</param>
        /// <param name="secondCondition">第二个选项条件；为空表示无条件。</param>
        /// <returns>未写入 AssetDatabase 的临时 DialogueAsset。</returns>
        public DialogueAsset CreateChoice(DialogueSpeaker speaker, DialogueCondition firstCondition = null,
            DialogueCondition secondCondition = null)
        {
            DialogueAsset asset = Track(ScriptableObject.CreateInstance<DialogueAsset>());
            DialogueEntryNode entry = Track(ScriptableObject.CreateInstance<DialogueEntryNode>());
            DialogueSpeechNode speech = CreateSpeech(speaker,
                "这是一段足够长的测试文本，用于覆盖打字机的禁止跳过时间并观察选项延迟显示。\n第二行继续验证正文区域。");
            DialogueChoiceNode firstChoice = CreateChoice("继续对话", CreateEnd(), firstCondition);
            DialogueChoiceNode secondChoice = CreateChoice("结束对话", CreateEnd(), secondCondition);
            speech.AddChoice(firstChoice);
            speech.AddChoice(secondChoice);
            asset.SetEntryNode(entry);
            asset.AddNode(speech);
            asset.AddNode(firstChoice);
            asset.AddNode(secondChoice);
            entry.SetFirstSpeechNode(speech);
            asset.EnsureStableIds();
            return asset;
        }

        /// <summary>创建并登记一条对白节点。</summary>
        /// <param name="speaker">Speaker 资产。</param>
        /// <param name="text">对白正文。</param>
        /// <returns>新对白节点。</returns>
        private DialogueSpeechNode CreateSpeech(DialogueSpeaker speaker, string text)
        {
            DialogueSpeechNode speech = Track(ScriptableObject.CreateInstance<DialogueSpeechNode>());
            speech.Configure(speaker, text, null, 0f);
            return speech;
        }

        /// <summary>创建并登记一个结束节点。</summary>
        /// <returns>新结束节点。</returns>
        private DialogueEndNode CreateEnd() => Track(ScriptableObject.CreateInstance<DialogueEndNode>());

        /// <summary>创建带条件的选项节点并通过反射写入其 SerializeReference 列表。</summary>
        /// <param name="text">选项文本。</param>
        /// <param name="target">选项目标。</param>
        /// <param name="condition">可选条件。</param>
        /// <returns>新选项节点。</returns>
        private DialogueChoiceNode CreateChoice(string text, DialogueNode target, DialogueCondition condition)
        {
            DialogueChoiceNode choice = Track(ScriptableObject.CreateInstance<DialogueChoiceNode>());
            choice.Configure(text);
            choice.SetTargetNode(target);
            if (condition != null)
                SetField(choice, "conditions", new List<DialogueCondition> { condition });
            return choice;
        }

        /// <summary>登记一个临时 Unity 对象，统一由 Dispose 清理。</summary>
        /// <typeparam name="TObject">对象类型。</typeparam>
        /// <param name="objectToTrack">需要登记的对象。</param>
        /// <returns>原对象。</returns>
        private TObject Track<TObject>(TObject objectToTrack) where TObject : UnityEngine.Object
        {
            temporaryObjects.Add(objectToTrack);
            return objectToTrack;
        }

        #endregion

        #region 运行与输入

        /// <summary>创建两个场景参与者并通过实际 DialogueInteractable 私有入口启动对话。</summary>
        /// <param name="asset">待启动的临时对话图。</param>
        /// <param name="playerSpeaker">发起者 Speaker。</param>
        /// <param name="npcSpeaker">NPC Speaker。</param>
        /// <returns>实际创建的会话；启动失败时返回空。</returns>
        public DialogueSession StartThroughInteractable(DialogueAsset asset, DialogueSpeaker playerSpeaker,
            DialogueSpeaker npcSpeaker)
        {
            GameObject player = Track(new GameObject("DialogueEditorTest_Player"));
            DialogueParticipant playerParticipant = player.AddComponent<DialogueParticipant>();
            SetField(playerParticipant, "speaker", playerSpeaker);

            GameObject npc = Track(new GameObject("DialogueEditorTest_Npc"));
            DialogueParticipant npcParticipant = npc.AddComponent<DialogueParticipant>();
            SetField(npcParticipant, "speaker", npcSpeaker);
            DialogueInteractable interactable = npc.AddComponent<DialogueInteractable>();
            SetField(interactable, "dialogueAsset", asset);
            SetField(interactable, "participantRoot", npc.transform);

            MethodInfo method = typeof(DialogueInteractable).GetMethod("TryStartDialogue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMemberException(typeof(DialogueInteractable).FullName, "TryStartDialogue");
            bool started = (bool)method.Invoke(interactable, new object[] { player });
            return started ? System.CurrentSession : null;
        }

        /// <summary>用两个临时 DialogueParticipant 通过公开 DialogueSystem API 启动对话。</summary>
        /// <param name="asset">待启动的对话图。</param>
        /// <param name="speaker">首句使用的 Speaker。</param>
        /// <param name="voiceClip">可选的首句语音。</param>
        /// <returns>实际创建的会话。</returns>
        public DialogueSession StartDirect(DialogueAsset asset, DialogueSpeaker speaker, AudioClip voiceClip = null,
            DialogueSpeaker initiatorSpeaker = null, IAnimationPlayer animationPlayer = null,
            AnimationClip animationClip = null)
        {
            GameObject player = Track(new GameObject("DialogueEditorTest_Player"));
            DialogueParticipant playerParticipant = player.AddComponent<DialogueParticipant>();
            SetField(playerParticipant, "speaker", initiatorSpeaker ?? speaker);

            GameObject npc = Track(new GameObject("DialogueEditorTest_Npc"));
            DialogueParticipant npcParticipant = npc.AddComponent<DialogueParticipant>();
            SetField(npcParticipant, "speaker", speaker);
            if (voiceClip != null)
            {
                AudioSource source = npc.AddComponent<AudioSource>();
                source.playOnAwake = false;
                SetField(npcParticipant, "voiceAudioSource", source);
                SetFirstSpeechVoice(asset, voiceClip);
            }
            if (animationPlayer != null) npcParticipant.SetAnimationPlayer(animationPlayer);
            if (animationClip != null) SetFirstSpeechAnimation(asset, animationClip);

            DialogueStartResult result = System.TryStartDialogue(new DialogueRequest(
                asset, playerParticipant, new[] { npcParticipant }));
            return result.Succeeded ? result.Session : null;
        }

        /// <summary>为临时图的首个 SpeechNode 注入测试语音，不写入任何项目资产。</summary>
        /// <param name="asset">目标内存对话图。</param>
        /// <param name="voiceClip">测试音频。</param>
        private static void SetFirstSpeechVoice(DialogueAsset asset, AudioClip voiceClip)
        {
            for (int index = 0; index < asset.Nodes.Count; index++)
            {
                if (asset.Nodes[index] is DialogueSpeechNode speech)
                {
                    SetField(speech, "voiceClip", voiceClip);
                    return;
                }
            }
            throw new InvalidOperationException("测试图没有 SpeechNode，无法注入语音。");
        }

        /// <summary>为临时图的首个 SpeechNode 注入测试动画，不写入任何项目资产。</summary>
        /// <param name="asset">目标内存对话图。</param>
        /// <param name="animationClip">测试动画。</param>
        private static void SetFirstSpeechAnimation(DialogueAsset asset, AnimationClip animationClip)
        {
            for (int index = 0; index < asset.Nodes.Count; index++)
            {
                if (asset.Nodes[index] is DialogueSpeechNode speech)
                {
                    SetField(speech, "animationClip", animationClip);
                    return;
                }
            }
            throw new InvalidOperationException("测试图没有 SpeechNode，无法注入动画。");
        }

        /// <summary>登记临时动画对象，保证测试结束时由夹具统一销毁。</summary>
        /// <param name="clip">临时动画片段。</param>
        /// <returns>原动画片段。</returns>
        public AnimationClip TrackAnimationClip(AnimationClip clip) => Track(clip);

        /// <summary>向指定 Choice 写入临时 Action 列表。</summary>
        /// <param name="choice">目标 Choice。</param>
        /// <param name="actions">测试动作。</param>
        public static void SetChoiceActions(DialogueChoiceNode choice, params DialogueAction[] actions) =>
            SetField(choice, "actions", new List<DialogueAction>(actions ?? Array.Empty<DialogueAction>()));

        /// <summary>登记临时音频对象，保证测试结束时由夹具统一销毁。</summary>
        /// <param name="clip">临时音频片段。</param>
        /// <returns>原音频片段。</returns>
        public AudioClip TrackAudioClip(AudioClip clip) => Track(clip);

        /// <summary>通过真实 EventSystem 向背景推进按钮发送一次 PointerClick。</summary>
        public void ClickAdvance()
        {
            if (EventSystem.current == null) throw new InvalidOperationException("场景缺少 EventSystem。");
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width / 2f, Screen.height / 2f),
                button = PointerEventData.InputButton.Left,
                eligibleForClick = true
            };
            var raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, raycastResults);
            GameObject hitTarget = null;
            for (int index = 0; index < raycastResults.Count; index++)
            {
                Button hitButton = raycastResults[index].gameObject.GetComponentInParent<Button>();
                if (ReferenceEquals(hitButton, Data.AdvanceButton))
                {
                    hitTarget = hitButton.gameObject;
                    break;
                }
            }

            if (hitTarget == null)
                throw new InvalidOperationException("鼠标位置没有命中 DialogueInteractPanel 的 AdvanceButton。");

            // 通过真实射线结果驱动 PointerDown/Up/Click，覆盖 GraphicRaycaster 与 Button 的交互判断。
            pointer.pointerPress = hitTarget;
            ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>通过真实 EventSystem 向当前焦点对象发送 Submit。</summary>
        public void SubmitFocused()
        {
            if (EventSystem.current == null) throw new InvalidOperationException("场景缺少 EventSystem。");
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject,
                new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        /// <summary>向当前 EventSystem Selection 发送一次真实 Move 事件并返回移动后的对象。</summary>
        /// <param name="direction">要发送的上下导航方向。</param>
        /// <returns>EventSystem 处理后的当前选中对象。</returns>
        public GameObject MoveFocused(MoveDirection direction)
        {
            EventSystem eventSystem = EventSystem.current ??
                throw new InvalidOperationException("场景缺少 EventSystem。");
            GameObject selected = eventSystem.currentSelectedGameObject ??
                throw new InvalidOperationException("发送 Move 前没有当前 EventSystem Selection。");
            AxisEventData eventData = new(eventSystem) { moveDir = direction, selectedObject = selected };
            ExecuteEvents.Execute(selected, eventData, ExecuteEvents.moveHandler);
            return eventSystem.currentSelectedGameObject;
        }

        /// <summary>向指定 Dialogue Choice 行派发真实类型的鼠标进入事件，建立该行的移动锚点。</summary>
        /// <param name="rowIndex">Choice 行索引。</param>
        /// <param name="position">鼠标屏幕坐标。</param>
        public void PointerEnterChoice(int rowIndex, Vector2 position)
        {
            Button button = GetChoiceButton(rowIndex);
            ExtendedPointerEventData eventData = CreateMousePointerEvent(position);
            ExecuteEvents.Execute(button.gameObject, eventData, ExecuteEvents.pointerEnterHandler);
        }

        /// <summary>向指定 Dialogue Choice 行派发一次鼠标 Move，验证行内位移阈值和 Selection 切换。</summary>
        /// <param name="rowIndex">Choice 行索引。</param>
        /// <param name="position">本次鼠标移动后的屏幕坐标。</param>
        /// <param name="delta">相对上一次 Pointer Move 的屏幕位移。</param>
        public void PointerMoveChoice(int rowIndex, Vector2 position, Vector2 delta)
        {
            Button button = GetChoiceButton(rowIndex);
            ExtendedPointerEventData eventData = CreateMousePointerEvent(position);
            eventData.delta = delta;
            ExecuteEvents.Execute(button.gameObject, eventData, ExecuteEvents.pointerMoveHandler);
        }

        /// <summary>向指定 Dialogue Choice 行派发鼠标离开事件，验证重新进入时不会沿用旧锚点。</summary>
        /// <param name="rowIndex">Choice 行索引。</param>
        /// <param name="position">鼠标离开时的屏幕坐标。</param>
        public void PointerExitChoice(int rowIndex, Vector2 position)
        {
            Button button = GetChoiceButton(rowIndex);
            ExtendedPointerEventData eventData = CreateMousePointerEvent(position);
            ExecuteEvents.Execute(button.gameObject, eventData, ExecuteEvents.pointerExitHandler);
        }

        /// <summary>创建带 Input System 鼠标类型的 Pointer 事件，避免测试绕过 OptionChoice 的输入过滤。</summary>
        /// <param name="position">鼠标屏幕坐标。</param>
        /// <returns>可发送到行组件的鼠标事件。</returns>
        private static ExtendedPointerEventData CreateMousePointerEvent(Vector2 position)
        {
            EventSystem eventSystem = EventSystem.current ??
                throw new InvalidOperationException("场景缺少 EventSystem。");
            return new ExtendedPointerEventData(eventSystem)
            {
                pointerId = 1001,
                position = position,
                pointerType = UIPointerType.MouseOrPen,
                button = PointerEventData.InputButton.Left
            };
        }

        /// <summary>读取当前 Dialogue Choice 行 Button，索引错误时立即暴露测试配置问题。</summary>
        /// <param name="rowIndex">Choice 行索引。</param>
        /// <returns>指定行 Button。</returns>
        private Button GetChoiceButton(int rowIndex)
        {
            Button[] buttons = ChoiceRoot.GetComponentsInChildren<Button>(true);
            if (rowIndex < 0 || rowIndex >= buttons.Length)
                throw new ArgumentOutOfRangeException(nameof(rowIndex), rowIndex, "Dialogue Choice 行索引超出范围。");
            return buttons[rowIndex];
        }

        /// <summary>读取选项 Button TargetGraphic 经过 ColorTint 后的实际渲染颜色。</summary>
        /// <param name="button">需要检查的选项按钮。</param>
        /// <returns>CanvasRenderer 当前颜色。</returns>
        public static Color GetRenderedColor(Button button)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            if (button.targetGraphic == null)
                throw new InvalidOperationException("选项 Button 缺少 TargetGraphic，无法验证 Selection 高亮。");
            return button.targetGraphic.canvasRenderer.GetColor();
        }

        /// <summary>点击 Choice 根下第一个可交互 Button。</summary>
        /// <returns>找到并点击时返回 true。</returns>
        public bool ClickFirstChoice()
        {
            if (EventSystem.current == null) throw new InvalidOperationException("场景缺少 EventSystem。");
            Button[] buttons = ChoiceRoot.GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                if (!buttons[index].interactable) continue;

                RectTransform rect = buttons[index].transform as RectTransform;
                Vector2 position = RectTransformUtility.WorldToScreenPoint(
                    UIManager.Instance.Camera, rect != null ? rect.position : buttons[index].transform.position);
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = position,
                    button = PointerEventData.InputButton.Left,
                    eligibleForClick = true
                };
                var raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, raycastResults);
                for (int hitIndex = 0; hitIndex < raycastResults.Count; hitIndex++)
                {
                    Button hitButton = raycastResults[hitIndex].gameObject.GetComponentInParent<Button>();
                    if (!ReferenceEquals(hitButton, buttons[index])) continue;
                    GameObject hitTarget = hitButton.gameObject;
                    pointer.pointerPress = hitTarget;
                    ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(hitTarget, pointer, ExecuteEvents.pointerClickHandler);
                    return true;
                }
            }
            return false;
        }

        /// <summary>通过反射结束遗留会话，避免某个失败用例污染后续用例。</summary>
        public void EndSessionForCleanup()
        {
            DialogueSession session = System.CurrentSession;
            if (session == null || session.IsEnded) return;
            MethodInfo method = typeof(DialogueSession).GetMethod("End", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMemberException(typeof(DialogueSession).FullName, "End");
            method.Invoke(session, new object[] { "Editor 测试清理。", DialogueEndStatus.Failed });
        }

        #endregion

        #region 反射辅助与释放

        /// <summary>读取测试需要观测的私有字段。</summary>
        /// <typeparam name="TValue">字段类型。</typeparam>
        /// <param name="target">目标对象。</param>
        /// <param name="fieldName">字段名。</param>
        /// <returns>字段值。</returns>
        public static TValue GetField<TValue>(object target, string fieldName)
        {
            FieldInfo field = FindField(target, fieldName);
            return (TValue)field.GetValue(target);
        }

        /// <summary>设置测试夹具需要的私有序列化字段。</summary>
        /// <param name="target">目标对象。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="value">字段值。</param>
        public static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = FindField(target, fieldName);
            field.SetValue(target, value);
        }

        /// <summary>定位目标类型或基类中的实例字段，找不到时立即暴露测试配置错误。</summary>
        /// <param name="target">目标对象。</param>
        /// <param name="fieldName">字段名。</param>
        /// <returns>字段元数据。</returns>
        private static FieldInfo FindField(object target, string fieldName)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        /// <summary>释放临时场景对象和内存 ScriptableObject，不触碰项目资产。</summary>
        public void Dispose()
        {
            EndSessionForCleanup();
            for (int index = temporaryObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object item = temporaryObjects[index];
                if (item != null) UnityEngine.Object.Destroy(item);
            }
            temporaryObjects.Clear();
        }

        #endregion

        #region 测试命令

        /// <summary>测试 Condition 始终返回指定结果并记录 Context 架构。</summary>
        internal sealed class TestCondition : DialogueCondition
        {
            private readonly Func<DialogueCommandContext, DialogueConditionResult> evaluator;

            /// <summary>创建测试条件；私有无参构造避免污染命令 Drawer 类型菜单。</summary>
            private TestCondition() { }

            /// <summary>创建带评估委托的测试条件。</summary>
            /// <param name="evaluator">条件评估函数。</param>
            public TestCondition(Func<DialogueCommandContext, DialogueConditionResult> evaluator)
            {
                this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            }

            /// <summary>执行测试评估委托。</summary>
            /// <param name="context">真实 DialogueSystem 创建的上下文。</param>
            /// <returns>测试条件结果。</returns>
            public override DialogueConditionResult Evaluate(DialogueCommandContext context) => evaluator(context);
        }

        /// <summary>测试 Action 记录执行次数和实际 Context。</summary>
        internal sealed class TestAction : DialogueAction
        {
            private readonly Action<DialogueCommandContext> action;

            /// <summary>创建测试动作；私有无参构造避免污染命令 Drawer 类型菜单。</summary>
            private TestAction() { }

            /// <summary>创建带执行委托的测试动作。</summary>
            /// <param name="action">动作委托。</param>
            public TestAction(Action<DialogueCommandContext> action)
            {
                this.action = action ?? throw new ArgumentNullException(nameof(action));
            }

            /// <summary>执行测试动作委托。</summary>
            /// <param name="context">真实 DialogueSystem 创建的上下文。</param>
            public override void Execute(DialogueCommandContext context) => action(context);
        }

        /// <summary>记录对话动画调用顺序而不依赖场景 Animator 或 Animancer 图。</summary>
        internal sealed class RecordingAnimationPlayer : IAnimationPlayer
        {
            /// <summary>记录测试参数写入。</summary>
            public void SetFloatParameter(Animancer.StringAsset parameter, float value) { }
            /// <summary>播放 AnimationClip 的调用次数。</summary>
            public int PlayCount { get; private set; }
            /// <summary>停止 Action 层的调用次数。</summary>
            public int StopCount { get; private set; }
            /// <summary>最近一次播放的动画素材。</summary>
            public AnimationClip LastClip { get; private set; }

            /// <summary>记录 AnimationClip 播放请求；替身不创建真实 AnimancerState。</summary>
            /// <param name="layer">目标动画层。</param>
            /// <param name="clip">待播放动画。</param>
            /// <param name="fadeDuration">淡入时长。</param>
            /// <param name="fadeMode">Animancer 淡入模式。</param>
            /// <returns>测试替身不创建状态，因此返回空。</returns>
            public AnimancerState Play(AnimationLayerType layer, AnimationClip clip,
                float fadeDuration = 0f, FadeMode fadeMode = FadeMode.FromStart)
            {
                PlayCount++;
                LastClip = clip;
                return null;
            }

            /// <summary>记录 Transition 播放接口；本测试不使用 Transition。</summary>
            /// <param name="layer">目标动画层。</param>
            /// <param name="transition">待播放 Transition。</param>
            /// <returns>测试替身不创建状态，因此返回空。</returns>
            public AnimancerState Play(AnimationLayerType layer, ITransition transition) => null;

            /// <summary>记录带本次淡入时长的 Transition 播放接口；本测试不创建真实状态。</summary>
            /// <param name="layer">目标动画层。</param>
            /// <param name="transition">待播放 Transition。</param>
            /// <param name="fadeDuration">本次播放使用的淡入时长。</param>
            /// <returns>测试替身不创建状态，因此返回空。</returns>
            public AnimancerState Play(AnimationLayerType layer, ITransition transition,
                float fadeDuration) => null;

            /// <summary>测试替身不需要处理层权重。</summary>
            /// <param name="layer">目标动画层。</param>
            /// <param name="targetWeight">目标权重。</param>
            /// <param name="duration">过渡时长。</param>
            public void FadeLayer(AnimationLayerType layer, float targetWeight, float duration) { }

            /// <summary>记录固定 Action 层停止请求。</summary>
            /// <param name="layer">被停止的动画层。</param>
            public void StopLayer(AnimationLayerType layer) => StopCount++;
        }

        #endregion
    }
}
#endif
