#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Events;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using GlobalEventSystem = WS_Modules.CustomEventSystem.EventSystem;
using WS_Modules.UIModule;

namespace RPG.DialogueSystemModule.Editor.Tests
{
    /// <summary>按真实窗口和 DialogueSystem 链路执行可重复的对话集成用例。</summary>
    internal static class DialogueEditorTestCases
    {
        #region 用例编排

        /// <summary>顺序执行核心窗口、打字机、Choice、语音和生命周期测试。</summary>
        /// <param name="fixture">当前 Play Mode 测试夹具。</param>
        /// <param name="report">增量测试报告。</param>
        /// <returns>EditorApplication.update 驱动的步骤枚举器。</returns>
        public static IEnumerator RunAll(DialogueEditorTestFixture fixture, DialogueEditorTestReport report)
        {
            string[] ids =
            {
                "PrefabBindings", "UIEventSystemBindings", "DialogueChoiceNavigation", "InteractableInitiator", "EventOrder", "TypingNaturalComplete",
                "NoSkipAndSkip", "ChoiceDelayedAndDisabled", "ChoiceActionAndContext", "DirectModeChoice",
                "FadeModeChoice", "VoiceLifecycle", "AnimationLifecycle", "StaleRevealInvalidation",
                "GameUILockEvents", "WindowDestroyAndReload"
            };
            Func<DialogueEditorTestFixture, IEnumerator>[] tests =
            {
                TestPrefabBindings, TestUIEventSystemBindings, TestDialogueChoiceNavigation, TestInteractableInitiator, TestEventOrder, TestTypingNaturalComplete,
                TestNoSkipAndSkip, TestChoiceDelayedAndDisabled, TestChoiceActionAndContext, TestDirectModeChoice,
                TestFadeModeChoice, TestVoiceLifecycle, TestAnimationLifecycle, TestStaleRevealInvalidation,
                TestGameUILockEvents, TestWindowDestroyAndReload
            };
            for (int index = 0; index < tests.Length; index++)
            {
                // C# 的 yield 不会自动展开嵌套 IEnumerator；这里逐项转发，确保每个断言实际执行。
                IEnumerator currentCase = RunCase(ids[index], fixture, report, tests[index]);
                while (currentCase.MoveNext()) yield return currentCase.Current;
            }
        }

        /// <summary>执行单个用例并把异常转换为带上下文的失败报告。</summary>
        /// <param name="id">用例标识。</param>
        /// <param name="fixture">测试夹具。</param>
        /// <param name="report">测试报告。</param>
        /// <param name="test">用例步骤。</param>
        /// <returns>用例执行枚举器。</returns>
        private static IEnumerator RunCase(string id, DialogueEditorTestFixture fixture,
            DialogueEditorTestReport report, Func<DialogueEditorTestFixture, IEnumerator> test)
        {
            var result = new DialogueEditorTestCaseResult { Id = id, Status = "Failed" };
            report.CurrentCaseId = id;
            result.Expected = "该用例的全部断言通过。";
            double startedAt = EditorApplication.timeSinceStartup;
            Exception failure = null;
            IEnumerator steps = null;
            try { steps = test(fixture); }
            catch (Exception exception) { failure = exception; }
            while (failure == null && steps != null)
            {
                bool moved;
                object current = null;
                try { moved = steps.MoveNext(); if (moved) current = steps.Current; }
                catch (Exception exception) { failure = exception; moved = false; }
                if (!moved) break;
                yield return current;
            }
            if (failure == null)
            {
                result.Status = "Passed";
                result.Message = "全部断言通过。";
            }
            else
            {
                result.Status = "Failed";
                result.Message = failure.Message;
                result.Exception = failure.ToString();
                result.Actual = failure.Message;
                CaptureObservation(result, fixture);
                fixture.EndSessionForCleanup();
            }
            if (failure == null)
            {
                result.Actual = "全部断言通过。";
                CaptureObservation(result, fixture);
            }
            result.DurationSeconds = (float)(EditorApplication.timeSinceStartup - startedAt);
            report.Add(result);
            report.CurrentCaseId = string.Empty;
            yield return null;
        }

        /// <summary>把失败现场的运行时状态复制到报告，便于 MCP 查询后定位时序和生命周期问题。</summary>
        /// <param name="result">待补充的用例结果。</param>
        /// <param name="testFixture">当前测试夹具。</param>
        private static void CaptureObservation(DialogueEditorTestCaseResult result,
            DialogueEditorTestFixture testFixture)
        {
            if (result == null || testFixture == null) return;
            try
            {
                DialogueSession session = testFixture.System.CurrentSession;
                result.SessionId = session?.SessionId ?? string.Empty;
                result.NodeId = session?.CurrentSpeech?.NodeId ?? string.Empty;
                result.WindowVisible = testFixture.Window != null && testFixture.Window.Visible;
                result.SelectedObject = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.name ?? string.Empty;
                result.TypeWriterState = testFixture.TypeWriter != null
                    ? testFixture.TypeWriter.CurrentState.ToString()
                    : string.Empty;
                result.ChoiceVisible = testFixture.ChoiceRoot != null && testFixture.ChoiceRoot.gameObject.activeSelf;
            }
            catch (Exception exception)
            {
                result.Actual += $"（现场状态采集失败：{exception.Message}）";
            }
        }

        #endregion

        #region 基础与参与者

        /// <summary>确认真实窗口的单窗口层级和 TypeWriter 显式引用。</summary>
        private static IEnumerator TestPrefabBindings(DialogueEditorTestFixture fixture)
        {
            Assert(fixture.Window != null && fixture.Window.GameObject != null, "DialogueWindow 未由 UIManager 预加载。");
            Assert(fixture.Data.SpeakContentTypeWriter != null, "正文没有 TypeWriter 引用。");
            Assert(fixture.Data.SpeakContentTypeWriter.gameObject == fixture.Data.SpeakContentTMP_Text.gameObject,
                "TypeWriter 与正文 TMP 不在同一对象。");
            Assert(fixture.Data.AdvanceButton != null && fixture.Data.DialogueChoiceRootTransform != null,
                "DialogueWindowDataComponent 的交互引用不完整。");
            Assert(UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow) &&
                hudWindow.GameObject.GetComponent<HUDWindowController>() != null,
                "HUDWindow prefab 缺少 HUDWindowController。");
            Assert(UIManager.Instance.TryGetWindow<ChoiceWindow>(out ChoiceWindow choiceWindow),
                "ChoiceWindow 未由 UIManager 预加载，无法比较 ChoiceRoot 布局。");
            ChoiceWindowDataComponent choiceData = choiceWindow.GameObject.GetComponent<ChoiceWindowDataComponent>();
            RectTransform dialogueRoot = fixture.ChoiceRoot as RectTransform;
            RectTransform choiceRoot = choiceData?.ChoiceRootTransform as RectTransform;
            Assert(dialogueRoot != null && choiceRoot != null, "Dialogue/Choice Root 缺少 RectTransform。");
            Assert(Vector2.Distance(dialogueRoot.anchorMin, choiceRoot.anchorMin) < 0.001f &&
                Vector2.Distance(dialogueRoot.anchorMax, choiceRoot.anchorMax) < 0.001f &&
                Vector2.Distance(dialogueRoot.anchoredPosition, choiceRoot.anchoredPosition) < 0.001f &&
                Vector2.Distance(dialogueRoot.sizeDelta, choiceRoot.sizeDelta) < 0.001f &&
                Vector2.Distance(dialogueRoot.pivot, choiceRoot.pivot) < 0.001f,
                "DialogueChoiceRoot 的 RectTransform 没有照搬 ChoiceWindow.ChoiceRoot。");
            VerticalLayoutGroup dialogueLayout = fixture.ChoiceRoot.GetComponent<VerticalLayoutGroup>();
            VerticalLayoutGroup choiceLayout = choiceData.ChoiceRootTransform.GetComponent<VerticalLayoutGroup>();
            Assert(dialogueLayout != null && choiceLayout != null &&
                dialogueLayout.spacing == choiceLayout.spacing &&
                dialogueLayout.childForceExpandWidth == choiceLayout.childForceExpandWidth &&
                dialogueLayout.childForceExpandHeight == choiceLayout.childForceExpandHeight &&
                dialogueLayout.childControlWidth == choiceLayout.childControlWidth &&
                dialogueLayout.childControlHeight == choiceLayout.childControlHeight,
                "DialogueChoiceRoot 的 VerticalLayoutGroup 没有照搬 ChoiceWindow.ChoiceRoot。");
            Assert(fixture.ChoiceRoot.GetComponentsInChildren<Button>(true).Length >= 0,
                "Choice 根节点不可访问。");
            yield break;
        }

        /// <summary>确认场景只使用正式 Input Action 资产和唯一 UI 输入模块。</summary>
        private static IEnumerator TestUIEventSystemBindings(DialogueEditorTestFixture fixture)
        {
            InputSystemUIInputModule[] modules = UnityEngine.Object.FindObjectsOfType<InputSystemUIInputModule>(true);
            Assert(modules.Length == 1, $"场景中应只有一个 InputSystemUIInputModule，实际为 {modules.Length} 个。");

            InputSystemUIInputModule module = modules[0];
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem/InputSystem_Actions.inputactions");
            Assert(actions != null, "无法加载正式 InputSystem_Actions.inputactions。");
            Assert(ReferenceEquals(module.actionsAsset, actions), "UI 输入模块仍引用旧的或缺失的 InputActionAsset。");
            Assert(module.move != null && module.move.action != null && module.move.action.name == "Navigate",
                "UI 输入模块没有绑定 UI/Navigate。");
            Assert(module.submit != null && module.submit.action != null && module.submit.action.name == "Submit",
                "UI 输入模块没有绑定 UI/Submit。");
            Assert(!module.deselectOnBackgroundClick,
                "点击非按钮背景不应清除当前 UI Selection，否则无法继续上下导航。");
            yield return null;
        }

        /// <summary>验证 Dialogue Choice 的上下导航跳过不可用项并保持 EventSystem Selection。</summary>
        private static IEnumerator TestDialogueChoiceNavigation(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;
            fixture.TypeWriter.noSkipDuration = 0f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestNavigation");
            DialogueAsset asset = fixture.CreateChoice(speaker, null,
                new DialogueEditorTestFixture.TestCondition(_ => DialogueConditionResult.NotMet("导航跳过。")));
            fixture.StartDirect(asset, speaker);
            yield return WaitUntil(() => fixture.ChoiceRoot.gameObject.activeSelf, 2d);

            Button[] buttons = fixture.ChoiceRoot.GetComponentsInChildren<Button>(true);
            Assert(buttons.Length >= 2, "导航测试需要至少两个 Choice 行。");
            Assert(EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == buttons[0].gameObject,
                "Choice 展示后没有聚焦第一个可用选项。");
            yield return WaitUntil(() => IsColorClose(
                DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.selectedColor), 1d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.selectedColor),
                "Choice 首项已被 EventSystem 选中，但实际渲染颜色没有进入 Selected 状态。");

            GameObject afterDown = fixture.MoveFocused(MoveDirection.Down);
            Assert(afterDown == buttons[0].gameObject,
                "只有一个可用 Choice 时，Down 没有保持在该选项上。");
            yield return WaitSeconds(0.15d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.selectedColor),
                "只有一个可用 Choice 时，Selected 高亮不应因导航刷新丢失。");

            IReadOnlyList<DialogueChoiceSnapShot> choices = fixture.System.CurrentChoicePresentations;
            Assert(choices.Count == 2 && choices[0].IsAvailable && !choices[1].IsAvailable,
                "导航测试的可用状态没有按预期构造。");
            Assert(buttons[0].navigation.mode == Navigation.Mode.Explicit &&
                buttons[0].navigation.selectOnUp == buttons[0] &&
                buttons[0].navigation.selectOnDown == buttons[0],
                "不可用 Choice 没有被从显式上下导航链中跳过。");
            fixture.EndSessionForCleanup();
            yield return null;

            // 再以两个可用选项验证上下移动和首尾循环；前一段会话的 Selection 不得污染本次聚焦。
            fixture.StartDirect(fixture.CreateChoice(speaker), speaker);
            yield return WaitUntil(() => fixture.ChoiceRoot.gameObject.activeSelf, 2d);
            buttons = fixture.ChoiceRoot.GetComponentsInChildren<Button>(true);
            Assert(EventSystem.current.currentSelectedGameObject == buttons[0].gameObject,
                "第二次 Choice 展示没有重新聚焦首个可用项。");
            yield return WaitUntil(() => IsColorClose(
                DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.selectedColor), 1d);
            Assert(fixture.MoveFocused(MoveDirection.Down) == buttons[1].gameObject,
                "Down 没有移动到下一个可用 Choice。");
            yield return WaitSeconds(0.15d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[1]), buttons[1].colors.selectedColor) &&
                IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.normalColor),
                "Down 后 EventSystem Selection 与 Choice 实际渲染高亮不一致。");
            Assert(fixture.MoveFocused(MoveDirection.Up) == buttons[0].gameObject,
                "Up 没有返回上一个可用 Choice。");
            yield return WaitSeconds(0.15d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.selectedColor) &&
                IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[1]), buttons[1].colors.normalColor),
                "Up 后 EventSystem Selection 与 Choice 实际渲染高亮不一致。");
            Assert(fixture.MoveFocused(MoveDirection.Up) == buttons[1].gameObject,
                "Up 没有按循环规则从首项回到末项。");
            yield return WaitSeconds(0.15d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[1]), buttons[1].colors.selectedColor) &&
                IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.normalColor),
                "循环导航后实际高亮没有跟随最后选中项。");

            // 鼠标进入第二项但只移动少量距离时，不应抢走键盘当前 Selection。
            Assert(fixture.MoveFocused(MoveDirection.Down) == buttons[0].gameObject,
                "鼠标移动测试无法先将 Selection 定位到首项。");
            Vector2 pointerPosition = new(100f, 100f);
            fixture.PointerEnterChoice(1, pointerPosition);
            fixture.PointerMoveChoice(1, pointerPosition + new Vector2(4f, 0f), new Vector2(4f, 0f));
            Assert(EventSystem.current.currentSelectedGameObject == buttons[0].gameObject,
                "鼠标在选项内移动未达到阈值时不应改变当前 Selection。");

            // 达到 8 px 阈值后，鼠标所在行成为 Selection，后续键盘导航从该行继续。
            fixture.PointerMoveChoice(1, pointerPosition + new Vector2(9f, 0f), new Vector2(5f, 0f));
            Assert(EventSystem.current.currentSelectedGameObject == buttons[1].gameObject,
                "鼠标在选项内移动达到阈值后没有切换 EventSystem Selection。");
            yield return WaitSeconds(0.15d);
            Assert(IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[1]), buttons[1].colors.selectedColor) &&
                IsColorClose(DialogueEditorTestFixture.GetRenderedColor(buttons[0]), buttons[0].colors.normalColor),
                "鼠标移动切换后实际渲染高亮没有跟随 EventSystem Selection。");

            // 键盘从第二项循环到首项时，鼠标仍停在第二项；再次移动达到阈值应能切回第二项。
            Assert(fixture.MoveFocused(MoveDirection.Down) == buttons[0].gameObject,
                "鼠标切换后的 Down 没有从当前 Selection 继续循环。");
            fixture.PointerMoveChoice(1, pointerPosition + new Vector2(18f, 0f), new Vector2(9f, 0f));
            Assert(EventSystem.current.currentSelectedGameObject == buttons[1].gameObject,
                "键盘移开 Selection 后，鼠标在原选项内再次移动没有切回该选项。");

            // 离开并重新进入第二项后，旧锚点必须失效，重新移动不足阈值不能立即抢焦点。
            Assert(fixture.MoveFocused(MoveDirection.Down) == buttons[0].gameObject,
                "PointerExit 测试无法先将 Selection 定位到首项。");
            fixture.PointerExitChoice(1, pointerPosition + new Vector2(18f, 0f));
            Vector2 reenterPosition = new(220f, 180f);
            fixture.PointerEnterChoice(1, reenterPosition);
            fixture.PointerMoveChoice(1, reenterPosition + new Vector2(4f, 0f), new Vector2(4f, 0f));
            Assert(EventSystem.current.currentSelectedGameObject == buttons[0].gameObject,
                "重新进入选项后移动未达到阈值时不应沿用旧锚点抢走 Selection。");
            fixture.EndSessionForCleanup();
            yield return null;
        }

        /// <summary>通过 DialogueInteractable 私有交互入口确认 Initiator 被合并到 Request。</summary>
        private static IEnumerator TestInteractableInitiator(DialogueEditorTestFixture fixture)
        {
            DialogueSpeaker player = fixture.CreateSpeaker("EditorTestPlayer");
            DialogueSpeaker npc = fixture.CreateSpeaker("EditorTestNpc");
            DialogueAsset asset = fixture.CreateLinear(npc, "NPC 首句。");
            DialogueSession session = fixture.StartThroughInteractable(asset, player, npc);
            Assert(session != null, "DialogueInteractable 未能启动临时对话。");
            Assert(session.Request.Initiator != null && session.Request.Participants.Count == 2,
                "Request 未包含 Initiator 和 NPC 两个参与者。");
            Assert(session.Request.FindParticipant(npc) != null, "无法按 Speaker SO 找到 NPC。");
            fixture.EndSessionForCleanup();
            yield return null;
        }

        /// <summary>记录首句事实事件顺序并确认首句只有一次 SpeechPresented。</summary>
        private static IEnumerator TestEventOrder(DialogueEditorTestFixture fixture)
        {
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestSpeaker");
            DialogueAsset asset = fixture.CreateLinear(speaker, "事件顺序测试。");
            var events = new List<string>();
            fixture.System.SpeechPresented += OnSpeech;
            fixture.System.Started += OnStarted;
            try
            {
                DialogueSession session = fixture.StartDirect(asset, speaker);
                Assert(session != null, "DialogueSystem 未能启动临时对话。");
                Assert(events.Count == 2 && events[0] == "Speech" && events[1] == "Started",
                    "首句事件顺序不是 SpeechPresented -> Started。");
            }
            finally
            {
                fixture.System.SpeechPresented -= OnSpeech;
                fixture.System.Started -= OnStarted;
                fixture.EndSessionForCleanup();
            }
            yield return null;

            void OnSpeech(DialogueSpeechPresentedEvent _) => events.Add("Speech");
            void OnStarted(DialogueStartedEvent _) => events.Add("Started");
        }

        #endregion

        #region 打字机与输入

        /// <summary>验证 Typing 模式最终显示完整正文，并在完成后允许推进。</summary>
        private static IEnumerator TestTypingNaturalComplete(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Typing;
            fixture.TypeWriter.typingSpeed = 100f;
            fixture.TypeWriter.noSkipDuration = 0f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestTyping");
            string text = "自然完成的正文，用于检查所有字符最终都被显示。";
            fixture.StartDirect(fixture.CreateLinear(speaker, text), speaker);
            yield return WaitUntil(() => fixture.TypeWriter.CurrentState == TMProTypeWriter.WriterState.Completed &&
                !fixture.IsSpeechRevealing, 5d);
            Assert(fixture.Data.SpeakContentTMP_Text.text == text, "自然完成后的正文内容不一致。");
            bool buttonClicked = false;
            UnityAction clickMarker = () => buttonClicked = true;
            fixture.Data.AdvanceButton.onClick.AddListener(clickMarker);
            try
            {
                fixture.ClickAdvance();
                yield return WaitUntil(() => fixture.System.CurrentSession == null, 2d);
                Assert(fixture.System.CurrentSession == null,
                    $"完成文本后点击没有进入结束节点。Speech={fixture.System.CurrentSession?.CurrentSpeech?.NodeId ?? "<null>"}，State={fixture.System.CurrentSession?.State.ToString() ?? "<null>"}，WindowVisible={fixture.Window.Visible}，ButtonOnClick={buttonClicked}。");
            }
            finally
            {
                fixture.Data.AdvanceButton.onClick.RemoveListener(clickMarker);
            }
        }

        /// <summary>验证禁止跳过窗口、首次 Skip 和第二次推进的输入语义。</summary>
        private static IEnumerator TestNoSkipAndSkip(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Typing;
            fixture.TypeWriter.typingSpeed = 100f;
            fixture.TypeWriter.noSkipDuration = 1f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestSkip");
            DialogueAsset asset = fixture.CreateLinear(speaker,
                "这是一段足够长的正文，确保第一次点击发生在打字过程和禁止跳过窗口内。最后直接进入结束节点。 ");
            Assert(!fixture.ChoiceRoot.gameObject.activeSelf, "Skip 测试开始前 ChoiceRoot 没有清理。");
            DialogueSession session = fixture.StartDirect(asset, speaker);
            Assert(session != null, "Skip 测试无法启动会话。");
            fixture.ClickAdvance();
            Assert(ReferenceEquals(fixture.System.CurrentSession, session), "NoSkipDuration 内点击推进了会话。");
            yield return WaitSeconds(1.1d);
            fixture.ClickAdvance();
            yield return WaitUntil(() => fixture.TypeWriter.CurrentState == TMProTypeWriter.WriterState.Completed &&
                !fixture.IsSpeechRevealing, 2d);
            Assert(ReferenceEquals(fixture.System.CurrentSession, session), "Skip 点击错误地推进到了下一节点。");
            fixture.ClickAdvance();
            yield return WaitUntil(() => fixture.System.CurrentSession == null, 2d);
            Assert(fixture.System.CurrentSession == null,
                $"文本完成后的点击未推进到结束节点。Speech={fixture.System.CurrentSession?.CurrentSpeech?.NodeId ?? "<null>"}，State={fixture.System.CurrentSession?.State.ToString() ?? "<null>"}，WindowVisible={fixture.Window.Visible}。");
        }

        #endregion

        #region Choice 与命令

        /// <summary>验证 Choice 延迟展示，同时确认不可用选项被置灰。</summary>
        private static IEnumerator TestChoiceDelayedAndDisabled(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Typing;
            fixture.TypeWriter.typingSpeed = 100f;
            fixture.TypeWriter.noSkipDuration = 0f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestChoice");
            var unavailable = new DialogueEditorTestFixture.TestCondition(_ =>
                DialogueConditionResult.NotMet("测试条件未满足。"));
            fixture.StartDirect(fixture.CreateChoice(speaker, null, unavailable), speaker);
            Assert(!fixture.ChoiceRoot.gameObject.activeSelf, "正文显示期间提前展示了 Choice。");
            yield return WaitUntil(() => fixture.ChoiceRoot.gameObject.activeSelf, 5d);
            Button[] buttons = fixture.ChoiceRoot.GetComponentsInChildren<Button>(true);
            int disabled = 0;
            for (int index = 0; index < buttons.Length; index++) if (!buttons[index].interactable) disabled++;
            Assert(disabled >= 1, "不可用 Choice 没有被置灰或禁用。");
            fixture.EndSessionForCleanup();
            yield return null;
        }

        /// <summary>验证 Choice Action 顺序执行并接收到真实 Architecture Context。</summary>
        private static IEnumerator TestChoiceActionAndContext(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestAction");
            var contextSeen = new List<bool>();
            DialogueAsset asset = fixture.CreateChoice(speaker);
            DialogueChoiceNode choice = FindFirstChoice(asset);
            DialogueEditorTestFixture.SetChoiceActions(choice,
                new DialogueEditorTestFixture.TestAction(context => contextSeen.Add(
                    ReferenceEquals(context.Architecture, RPG.Game.GameArchitecture.Interface))));
            fixture.StartDirect(asset, speaker);
            yield return WaitUntil(() => fixture.ChoiceRoot.gameObject.activeSelf, 2d);
            Assert(fixture.ClickFirstChoice(), "没有找到可用 Choice Button。");
            yield return WaitUntil(() => fixture.System.CurrentSession == null, 2d);
            Assert(contextSeen.Count == 1 && contextSeen[0], "Action 没有按顺序收到真实 Architecture Context。");
        }

        /// <summary>验证 Direct 模式会立即完成正文并显示 Choice。</summary>
        private static IEnumerator TestDirectModeChoice(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestDirect");
            fixture.StartDirect(fixture.CreateChoice(speaker), speaker);
            Assert(fixture.TypeWriter.CurrentState == TMProTypeWriter.WriterState.Completed,
                "Direct 模式没有立即完成。");
            Assert(fixture.ChoiceRoot.gameObject.activeSelf, "Direct 模式完成后 Choice 未展示。");
            fixture.EndSessionForCleanup();
            yield return null;
        }

        /// <summary>验证 Fade 模式等待淡入完成后再展示 Choice。</summary>
        private static IEnumerator TestFadeModeChoice(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Fade;
            fixture.TypeWriter.fadeSpeed = 1f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestFade");
            Assert(!fixture.ChoiceRoot.gameObject.activeSelf, "Fade 测试开始前 ChoiceRoot 没有清理。");
            fixture.StartDirect(fixture.CreateChoice(speaker), speaker);
            Assert(!fixture.ChoiceRoot.gameObject.activeSelf, "Fade 尚未完成时提前展示了 Choice。");
            yield return WaitUntil(() => fixture.ChoiceRoot.gameObject.activeSelf, 3d);
            yield return null;
            Assert(fixture.TypeWriter.CurrentState == TMProTypeWriter.WriterState.Completed &&
                !fixture.IsSpeechRevealing,
                $"Fade 完成后 TypeWriter 状态不正确：{fixture.TypeWriter.CurrentState}，IsSpeechRevealing={fixture.IsSpeechRevealing}。");
            fixture.EndSessionForCleanup();
            yield return null;
        }

        #endregion

        #region 语音、过期任务与生命周期

        /// <summary>验证 Skip 不停止语音，进入结束节点后停止语音。</summary>
        private static IEnumerator TestVoiceLifecycle(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestVoice");
            DialogueSpeaker playerSpeaker = fixture.CreateSpeaker("EditorTestVoicePlayer");
            AudioClip clip = fixture.TrackAudioClip(AudioClip.Create("DialogueEditorTestVoice", 4410, 1, 44100, false));
            DialogueAsset asset = fixture.CreateLinear(speaker, "带语音的测试正文。");
            DialogueSession session = fixture.StartDirect(asset, speaker, clip, playerSpeaker);
            Assert(session != null, "语音测试无法启动会话。");
            AudioSource source = FindTemporaryVoiceSource(fixture);
            Assert(source != null && source.isPlaying, "SpeechPresented 后 AudioSource 没有播放语音。");
            fixture.ClickAdvance();
            yield return WaitUntil(() => fixture.System.CurrentSession == null, 2d);
            Assert(!source.isPlaying, "进入结束节点后语音仍在播放。");
        }

        /// <summary>验证对白进入和结束时通过参与者 Context 播放并停止 Action 层动画。</summary>
        private static IEnumerator TestAnimationLifecycle(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestAnimation");
            DialogueSpeaker playerSpeaker = fixture.CreateSpeaker("EditorTestAnimationPlayer");
            AnimationClip clip = fixture.TrackAnimationClip(new AnimationClip { name = "DialogueEditorTestAnimation" });
            var animationPlayer = new DialogueEditorTestFixture.RecordingAnimationPlayer();
            DialogueAsset asset = fixture.CreateLinear(speaker, "带动画的测试正文。");
            DialogueSession session = fixture.StartDirect(asset, speaker, null, playerSpeaker, animationPlayer, clip);
            Assert(session != null, "动画测试无法启动会话。");
            Assert(animationPlayer.PlayCount == 1 && ReferenceEquals(animationPlayer.LastClip, clip),
                "SpeechPresented 后没有向 NPC 的 IAnimationPlayer 播放指定动画。");
            fixture.EndSessionForCleanup();
            Assert(animationPlayer.StopCount == 1, "会话结束后没有停止 NPC 的 Action 层动画。");
            yield return null;
        }

        /// <summary>在旧句仍可能存在异步任务时推进新句，确认旧任务不能回写正文。</summary>
        private static IEnumerator TestStaleRevealInvalidation(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Typing;
            fixture.TypeWriter.typingSpeed = 1f;
            fixture.TypeWriter.noSkipDuration = 0f;
            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestStale");
            DialogueAsset asset = fixture.CreateLinear(speaker, "旧句旧句旧句旧句旧句旧句旧句旧句。", "新句。");
            fixture.StartDirect(asset, speaker);
            fixture.System.Advance();
            yield return WaitUntil(() => fixture.TypeWriter.CurrentState == TMProTypeWriter.WriterState.Completed, 5d);
            Assert(fixture.Data.SpeakContentTMP_Text.text == "新句。", "旧句异步任务污染了新句正文。");
            fixture.EndSessionForCleanup();
        }

        /// <summary>验证 DialogueSystem 按同一会话来源对称发布 GameUILock Acquire 和 Release。</summary>
        private static IEnumerator TestGameUILockEvents(DialogueEditorTestFixture fixture)
        {
            fixture.TypeWriter.revealMode = TMProTypeWriter.RevealMode.Direct;

            // 先对真实 HUD prefab 验证多个独占来源的首个申请/最后释放语义。
            Assert(UIManager.Instance.TryGetWindow<HUDWindow>(out HUDWindow hudWindow),
                "GameUILock 测试找不到预加载 HUDWindow。");
            bool hudVisibleBeforeLocks = hudWindow.Visible;
            if (!hudVisibleBeforeLocks)
            {
                UIManager.Instance.PopUpWindow<HUDWindow>();
                yield return WaitUntil(() => hudWindow.Visible, 2d);
            }

            try
            {
                TriggerGameUILock("EditorTest:A", GameUILockOperation.Acquire);
                Assert(!hudWindow.Visible, "首个 GameUILock Acquire 没有隐藏 HUDWindow。");
                TriggerGameUILock("EditorTest:B", GameUILockOperation.Acquire);
                TriggerGameUILock("EditorTest:A", GameUILockOperation.Release);
                Assert(!hudWindow.Visible, "释放非最后来源后错误恢复了 HUDWindow。");
                TriggerGameUILock("EditorTest:Unknown", GameUILockOperation.Release);
                Assert(!hudWindow.Visible, "未知来源 Release 错误改变了 HUDWindow 状态。");
                TriggerGameUILock("EditorTest:B", GameUILockOperation.Release);
                Assert(hudWindow.Visible == hudVisibleBeforeLocks,
                    "最后来源释放后 HUDWindow 没有恢复锁定前的可见状态。");
            }
            finally
            {
                // 即使某个断言失败也要对称释放测试来源，避免污染后续用例。
                TriggerGameUILock("EditorTest:A", GameUILockOperation.Release);
                TriggerGameUILock("EditorTest:B", GameUILockOperation.Release);
                if (!hudVisibleBeforeLocks && hudWindow.Visible)
                    UIManager.Instance.HideWindow<HUDWindow>();
            }

            DialogueSpeaker speaker = fixture.CreateSpeaker("EditorTestGameUILock");
            var events = new List<GameUILockChangeRequestedEventArgs>();
            GlobalEventSystem.Register_Type<GameUILockChangeRequestedEventArgs>(
                typeof(GameUILockChangeRequestedEventArgs), events.Add);
            try
            {
                DialogueSession session = fixture.StartDirect(
                    fixture.CreateLinear(speaker, "GameUILock 测试。"), speaker);
                Assert(session != null && events.Count == 1 &&
                    events[0].Operation == GameUILockOperation.Acquire &&
                    events[0].SourceId == $"Dialogue:{session.SessionId}",
                    "启动对话没有发布正确的 GameUILock Acquire。");

                fixture.EndSessionForCleanup();
                Assert(events.Count == 2 &&
                    events[1].Operation == GameUILockOperation.Release &&
                    events[1].SourceId == events[0].SourceId,
                    "结束对话没有按相同 SourceId 发布 GameUILock Release。");
            }
            finally
            {
                GlobalEventSystem.UnRegister_Type<GameUILockChangeRequestedEventArgs>(
                    typeof(GameUILockChangeRequestedEventArgs), events.Add);
            }

            yield return null;
        }

        /// <summary>通过项目 Type EventCenter 发送一条 GameUILock 请求。</summary>
        /// <param name="sourceId">锁定来源 ID。</param>
        /// <param name="operation">申请或释放操作。</param>
        private static void TriggerGameUILock(string sourceId, GameUILockOperation operation)
        {
            GlobalEventSystem.EventTrigger_Type(
                typeof(GameUILockChangeRequestedEventArgs),
                new GameUILockChangeRequestedEventArgs(sourceId, operation));
        }

        /// <summary>销毁并重新预加载真实 DialogueWindow，验证窗口级 View 监听得到释放。</summary>
        private static IEnumerator TestWindowDestroyAndReload(DialogueEditorTestFixture fixture)
        {
            UIManager.Instance.DestroyWindow<DialogueWindow>();
            yield return WaitUntil(() => !UIManager.Instance.TryGetWindow<DialogueWindow>(out _), 3d);
            // DestroyWindow 会异步释放 Addressable 预制体；等待释放帧完成后再发起同名加载，避免与卸载竞态。
            yield return WaitSeconds(2d);
            UIManager.Instance.PreLoadWindowAsync<DialogueWindow>().Forget();
            yield return WaitUntil(() => UIManager.Instance.TryGetWindow<DialogueWindow>(out _), 10d);
            Assert(UIManager.Instance.TryGetWindow<DialogueWindow>(out DialogueWindow reloaded) && reloaded.GameObject != null,
                "DialogueWindow 销毁后无法重新预加载。");
        }

        /// <summary>按节点列表查找临时图第一个 Choice。</summary>
        /// <param name="asset">临时对话图。</param>
        /// <returns>第一个 Choice。</returns>
        private static DialogueChoiceNode FindFirstChoice(DialogueAsset asset)
        {
            for (int index = 0; index < asset.Nodes.Count; index++)
                if (asset.Nodes[index] is DialogueChoiceNode choice) return choice;
            throw new InvalidOperationException("测试图没有 ChoiceNode。");
        }

        /// <summary>找到夹具创建的语音 AudioSource。</summary>
        /// <param name="fixture">测试夹具。</param>
        /// <returns>临时 AudioSource。</returns>
        private static AudioSource FindTemporaryVoiceSource(DialogueEditorTestFixture fixture)
        {
            GameObject[] objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            for (int index = 0; index < objects.Length; index++)
                if (objects[index].name == "DialogueEditorTest_Npc") return objects[index].GetComponent<AudioSource>();
            return null;
        }

        #endregion

        #region 辅助步骤与断言

        /// <summary>等待一个同步条件在指定秒数内成立。</summary>
        /// <param name="condition">待等待条件。</param>
        /// <param name="timeoutSeconds">超时秒数。</param>
        /// <returns>逐 Editor 更新等待的枚举器。</returns>
        private static IEnumerator WaitUntil(Func<bool> condition, double timeoutSeconds)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= deadline)
                    throw new TimeoutException($"等待条件超过 {timeoutSeconds:0.##} 秒。");
                yield return null;
            }
        }

        /// <summary>按真实 Editor 时间等待，确保 NoSkipDuration 等时间行为被实际覆盖。</summary>
        /// <param name="seconds">等待秒数。</param>
        /// <returns>逐 Editor 更新等待的枚举器。</returns>
        private static IEnumerator WaitSeconds(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }

        /// <summary>比较 ColorTint 渐变结束后的渲染颜色，忽略极小的浮点误差。</summary>
        /// <param name="actual">CanvasRenderer 实际颜色。</param>
        /// <param name="expected">Button ColorBlock 目标颜色。</param>
        /// <returns>颜色各通道均在容差内时返回 true。</returns>
        private static bool IsColorClose(Color actual, Color expected)
        {
            const float tolerance = 0.02f;
            return Mathf.Abs(actual.r - expected.r) <= tolerance &&
                Mathf.Abs(actual.g - expected.g) <= tolerance &&
                Mathf.Abs(actual.b - expected.b) <= tolerance &&
                Mathf.Abs(actual.a - expected.a) <= tolerance;
        }

        /// <summary>抛出带有用例语义的断言异常。</summary>
        /// <param name="condition">断言条件。</param>
        /// <param name="message">失败说明。</param>
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        #endregion
    }
}
#endif
