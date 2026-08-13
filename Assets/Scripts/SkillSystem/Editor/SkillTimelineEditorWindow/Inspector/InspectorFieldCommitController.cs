#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理 Inspector 连续输入的本地草稿，并在回车或离开完整字段时统一提交。
    /// </summary>
    internal sealed class InspectorFieldCommitController : IDisposable
    {
        #region 状态

        private readonly List<IFieldBinding> bindings = new();
        private readonly Action cancelDraft;
        private bool disposed;
        private bool cancelling;

        #endregion

        /// <summary>
        /// 保存一次 Inspector 绘制周期的草稿取消入口。
        /// </summary>
        /// <param name="cancelDraft">Inspector 被刷新或销毁时用于清理预览草稿的回调。</param>
        internal InspectorFieldCommitController(Action cancelDraft)
        {
            this.cancelDraft = cancelDraft;
        }

        #region 字段绑定

        /// <summary>
        /// 为连续输入字段绑定草稿刷新、完成提交与 Escape 整体取消行为。
        /// </summary>
        /// <typeparam name="T">字段保存的值类型。</typeparam>
        /// <param name="field">需要管理编辑生命周期的字段。</param>
        /// <param name="draftChanged">输入过程中刷新本地草稿的回调。</param>
        /// <param name="commit">编辑完成后提交完整语义请求的回调。</param>
        internal void Bind<T>(BaseField<T> field, Action draftChanged, Action commit)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InspectorFieldCommitController));
            bindings.Add(new FieldBinding<T>(this, field, draftChanged, commit));
        }

        /// <summary>
        /// 为资源字段绑定 Object Picker 生命周期；选择窗口关闭前只保留本地最终选择。
        /// </summary>
        /// <param name="field">只接受指定 Unity Object 类型的资源字段。</param>
        /// <param name="commit">Picker 关闭或直接拖入资源后提交完整语义请求的回调。</param>
        internal void BindObjectField(ObjectField field, Action commit)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InspectorFieldCommitController));
            bindings.Add(new ObjectFieldBinding(this, field, commit));
        }

        /// <summary>
        /// 完成一个字段的编辑；提交前先清除 Dirty，避免同步 Inspector 刷新导致重复提交。
        /// </summary>
        /// <param name="binding">需要接受当前值并执行语义提交的字段绑定。</param>
        private void Commit(IFieldBinding binding)
        {
            if (disposed || cancelling || !binding.IsDirty) return;
            binding.AcceptCurrentValue();
            binding.Commit();
        }

        /// <summary>
        /// 恢复本轮全部字段的权威初值，并统一清除 Scene 预览草稿。
        /// </summary>
        private void CancelAll()
        {
            if (disposed || cancelling) return;
            cancelling = true;
            foreach (IFieldBinding binding in bindings) binding.RestoreAuthoritativeValue();
            cancelDraft?.Invoke();
            cancelling = false;
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 注销全部 UI 回调并清除未提交预览草稿；不会写入 Config 或创建 Undo。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (IFieldBinding binding in bindings) binding.Dispose();
            bindings.Clear();
            cancelDraft?.Invoke();
        }

        #endregion

        #region 字段绑定实现

        /// <summary>
        /// 为不同值类型字段提供统一的生命周期操作。
        /// </summary>
        private interface IFieldBinding : IDisposable
        {
            /// <summary>指示字段是否持有尚未提交的本地值。</summary>
            bool IsDirty { get; }
            /// <summary>接受当前本地值并清除 Dirty 状态。</summary>
            void AcceptCurrentValue();

            /// <summary>恢复本次绑定时记录的权威值。</summary>
            void RestoreAuthoritativeValue();

            /// <summary>执行字段对应的完整语义提交。</summary>
            void Commit();
        }

        /// <summary>
        /// 管理 ObjectField 与 Unity Object Picker 之间跨窗口、跨多次选择的提交生命周期。
        /// </summary>
        private sealed class ObjectFieldBinding : IFieldBinding
        {
            private const string ObjectSelectorUpdatedCommand = "ObjectSelectorUpdated";
            private const string ObjectSelectorClosedCommand = "ObjectSelectorClosed";
            private const string ObjectSelectorUssClassName = "unity-object-field__selector";

            private readonly InspectorFieldCommitController owner;
            private readonly ObjectField field;
            private readonly Action commit;
            private readonly EventCallback<ChangeEvent<UnityEngine.Object>> valueChangedCallback;
            private readonly EventCallback<ExecuteCommandEvent> executeCommandCallback;
            private readonly EventCallback<PointerDownEvent> pointerDownCallback;
            private UnityEngine.Object authoritativeValue;
            private IVisualElementScheduledItem scheduledCommit;
            private bool pickerActive;
            private bool disposed;

            public bool IsDirty { get; private set; }

            /// <summary>
            /// 创建资源字段绑定，并在命令的捕获阶段观察 Object Picker 更新与关闭消息。
            /// </summary>
            /// <param name="owner">统一完成提交和取消操作的控制器。</param>
            /// <param name="field">需要延迟 Picker 提交的资源字段。</param>
            /// <param name="commit">提交完整编辑请求的回调。</param>
            internal ObjectFieldBinding(InspectorFieldCommitController owner, ObjectField field, Action commit)
            {
                this.owner = owner;
                this.field = field ?? throw new ArgumentNullException(nameof(field));
                this.commit = commit;
                authoritativeValue = field.value;
                valueChangedCallback = OnValueChanged;
                executeCommandCallback = OnExecuteCommand;
                pointerDownCallback = OnPointerDown;
                field.RegisterValueChangedCallback(valueChangedCallback);
                field.RegisterCallback(executeCommandCallback, TrickleDown.TrickleDown);
                field.RegisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
            }

            /// <summary>
            /// 记录字段当前值；Picker 浏览期间不进入 Document，直接拖入或清空时立即提交。
            /// </summary>
            /// <param name="evt">ObjectField 当前值变化事件。</param>
            private void OnValueChanged(ChangeEvent<UnityEngine.Object> evt)
            {
                IsDirty = evt.newValue != authoritativeValue;
                if (pickerActive || !IsDirty) return;
                owner.Commit(this);
            }

            /// <summary>
            /// 在选择按钮按下时提前标记 Picker 生命周期，确保第一次 ValueChanged 也只更新本地字段。
            /// </summary>
            /// <param name="evt">ObjectField 后代元素收到的指针按下事件。</param>
            private void OnPointerDown(PointerDownEvent evt)
            {
                if (IsObjectSelectorTarget(evt.target as VisualElement)) pickerActive = true;
            }

            /// <summary>
            /// 观察 Object Picker 更新和关闭命令；关闭后的提交延迟到当前 UI 事件派发完成之后。
            /// </summary>
            /// <param name="evt">Unity Object Picker 发送的编辑器命令。</param>
            private void OnExecuteCommand(ExecuteCommandEvent evt)
            {
                if (evt.commandName == ObjectSelectorUpdatedCommand)
                {
                    pickerActive = true;
                    return;
                }

                if (evt.commandName != ObjectSelectorClosedCommand) return;
                pickerActive = false;
                ScheduleCommit();
            }

            /// <summary>
            /// 判断指针目标是否属于 ObjectField 的选择按钮，避免普通点击被误认为打开 Picker。
            /// </summary>
            /// <param name="target">本次指针事件的原始 VisualElement。</param>
            /// <returns>目标或其父级是否为 ObjectField 的选择按钮。</returns>
            private bool IsObjectSelectorTarget(VisualElement target)
            {
                for (VisualElement current = target; current != null && current != field; current = current.parent)
                {
                    if (current.ClassListContains(ObjectSelectorUssClassName)) return true;
                }

                return false;
            }

            /// <summary>
            /// 安排一次 Picker 最终提交，避免同步 Inspector 重建发生在 ExecuteCommand 派发栈中。
            /// </summary>
            private void ScheduleCommit()
            {
                scheduledCommit?.Pause();
                scheduledCommit = field.schedule.Execute(CommitScheduledValue);
            }

            /// <summary>
            /// 在下一次 UI 调度中提交最终选择；绑定已释放时不会写入资产。
            /// </summary>
            private void CommitScheduledValue()
            {
                scheduledCommit = null;
                if (!disposed) owner.Commit(this);
            }

            /// <summary>
            /// 接受 Picker 最终值并清除 Dirty，使后续关闭或焦点事件不会重复提交。
            /// </summary>
            public void AcceptCurrentValue()
            {
                authoritativeValue = field.value;
                IsDirty = false;
                pickerActive = false;
            }

            /// <summary>
            /// 恢复绑定时的权威资源，不发送新的 ValueChanged 或 Document 请求。
            /// </summary>
            public void RestoreAuthoritativeValue()
            {
                field.SetValueWithoutNotify(authoritativeValue);
                IsDirty = false;
                pickerActive = false;
                scheduledCommit?.Pause();
                scheduledCommit = null;
            }

            /// <summary>
            /// 执行 Drawer 提供的完整语义提交回调。
            /// </summary>
            public void Commit() => commit?.Invoke();

            /// <summary>
            /// 注销 ObjectField 事件并取消尚未执行的 Picker 关闭提交。
            /// </summary>
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                scheduledCommit?.Pause();
                scheduledCommit = null;
                field.UnregisterValueChangedCallback(valueChangedCallback);
                field.UnregisterCallback(executeCommandCallback, TrickleDown.TrickleDown);
                field.UnregisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
            }
        }

        /// <summary>
        /// 保存单个 BaseField 的权威初值、Dirty 状态和已注册回调。
        /// </summary>
        private sealed class FieldBinding<T> : IFieldBinding
        {
            private const int InvalidPointerId = -1;

            private readonly InspectorFieldCommitController owner;
            private readonly BaseField<T> field;
            private readonly Action draftChanged;
            private readonly Action commit;
            private readonly EventCallback<ChangeEvent<T>> valueChangedCallback;
            private readonly EventCallback<KeyDownEvent> keyDownCallback;
            private readonly EventCallback<FocusOutEvent> focusOutCallback;
            private readonly EventCallback<PointerDownEvent> pointerDownCallback;
            private readonly EventCallback<PointerUpEvent> pointerUpCallback;
            private readonly EventCallback<PointerCaptureOutEvent> pointerCaptureOutCallback;
            private T authoritativeValue;
            private int activePointerId = InvalidPointerId;
            private bool changedDuringPointer;

            public bool IsDirty { get; private set; }

            // 注册字段事件；KeyDown 使用 TrickleDown 以接收 Vector 子输入框的按键。
            /// <summary>
            /// 注册连续输入字段的草稿、键盘、焦点与指针提交生命周期。
            /// </summary>
            /// <param name="owner">统一协调提交与取消的控制器。</param>
            /// <param name="field">需要管理本地草稿的字段。</param>
            /// <param name="draftChanged">字段值变化时刷新预览草稿的回调。</param>
            /// <param name="commit">编辑完成后提交完整请求的回调。</param>
            internal FieldBinding(InspectorFieldCommitController owner, BaseField<T> field,
                Action draftChanged, Action commit)
            {
                this.owner = owner;
                this.field = field;
                this.draftChanged = draftChanged;
                this.commit = commit;
                authoritativeValue = field.value;
                valueChangedCallback = OnValueChanged;
                keyDownCallback = OnKeyDown;
                focusOutCallback = OnFocusOut;
                pointerDownCallback = OnPointerDown;
                pointerUpCallback = OnPointerUp;
                pointerCaptureOutCallback = OnPointerCaptureOut;
                field.RegisterValueChangedCallback(valueChangedCallback);
                field.RegisterCallback(keyDownCallback, TrickleDown.TrickleDown);
                field.RegisterCallback(focusOutCallback, TrickleDown.TrickleDown);
                field.RegisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
                field.RegisterCallback(pointerUpCallback, TrickleDown.TrickleDown);
                field.RegisterCallback(pointerCaptureOutCallback, TrickleDown.TrickleDown);
            }

            // 任意连续输入只更新本地 Dirty 与预览草稿，不进入 Document。
            /// <summary>
            /// 将连续输入记录为本地草稿，并在指针编辑周期内标记数值已变化。
            /// </summary>
            /// <param name="_">字段值变化事件。</param>
            private void OnValueChanged(ChangeEvent<T> _)
            {
                IsDirty = true;
                if (activePointerId != InvalidPointerId) changedDuringPointer = true;
                draftChanged?.Invoke();
            }

            // 开启一次 Pointer 编辑周期；已有键盘草稿不会因此被视为鼠标拖拽修改。
            /// <summary>
            /// 开启一次指针编辑周期，区分鼠标拖拽与已有键盘草稿。
            /// </summary>
            /// <param name="evt">字段收到的指针按下事件。</param>
            private void OnPointerDown(PointerDownEvent evt)
            {
                if (activePointerId != InvalidPointerId) return;
                activePointerId = evt.pointerId;
                changedDuringPointer = false;
            }

            // 鼠标松开时仅提交本次 Pointer 周期实际改变过的字段值。
            /// <summary>
            /// 在鼠标松开时完成本次确实发生变化的指针编辑。
            /// </summary>
            /// <param name="evt">字段收到的指针抬起事件。</param>
            private void OnPointerUp(PointerUpEvent evt)
            {
                if (activePointerId != evt.pointerId) return;
                CompletePointerEdit();
            }

            // Pointer Capture 丢失是拖出字段松手的兜底完成信号。
            /// <summary>
            /// 在 Pointer Capture 丢失时兜底完成拖出字段后的编辑。
            /// </summary>
            /// <param name="evt">字段收到的 Pointer Capture 丢失事件。</param>
            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (activePointerId != evt.pointerId) return;
                CompletePointerEdit();
            }

            // 先重置 Pointer 状态再提交，避免同步 Inspector 重建时后续事件重复进入。
            /// <summary>
            /// 先清理指针状态，再按本周期变化状态执行一次提交。
            /// </summary>
            private void CompletePointerEdit()
            {
                bool shouldCommit = changedDuringPointer;
                ResetPointerState();
                if (shouldCommit) owner.Commit(this);
            }

            // 清除当前 Pointer 编辑周期，不影响键盘输入产生的 Dirty 状态。
            /// <summary>
            /// 清除当前指针编辑周期，但保留键盘输入产生的 Dirty 状态。
            /// </summary>
            private void ResetPointerState()
            {
                activePointerId = InvalidPointerId;
                changedDuringPointer = false;
            }

            // Enter 提交当前字段；Escape 恢复本轮全部字段且阻止 TextInput 继续处理按键。
            /// <summary>
            /// 处理 Enter 提交与 Escape 取消，并阻止文本输入继续消费完成按键。
            /// </summary>
            /// <param name="evt">字段或其子输入框收到的按键事件。</param>
            private void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    owner.CancelAll();
                    evt.StopImmediatePropagation();
                    return;
                }

                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                owner.Commit(this);
                evt.StopImmediatePropagation();
            }

            // 只有焦点离开整个复合字段才提交，Vector3 的 X/Y/Z 内部切换不会触发提交。
            /// <summary>
            /// 仅在焦点离开整个复合字段时提交，避免分量间切换导致提前写入。
            /// </summary>
            /// <param name="evt">字段收到的焦点离开事件。</param>
            private void OnFocusOut(FocusOutEvent evt)
            {
                if (evt.relatedTarget is VisualElement next && field.Contains(next)) return;
                owner.Commit(this);
            }

            /// <summary>
            /// 将当前本地值作为本次编辑完成后的基准并清除 Dirty。
            /// </summary>
            public void AcceptCurrentValue()
            {
                authoritativeValue = field.value;
                IsDirty = false;
                ResetPointerState();
            }

            /// <summary>
            /// 使用无通知写入恢复初值，避免取消过程再次产生草稿事件。
            /// </summary>
            public void RestoreAuthoritativeValue()
            {
                field.SetValueWithoutNotify(authoritativeValue);
                IsDirty = false;
                ResetPointerState();
            }

            /// <summary>
            /// 执行 Drawer 提供的完整语义提交回调。
            /// </summary>
            public void Commit() => commit?.Invoke();

            /// <summary>
            /// 注销本字段全部回调，不修改字段值或资产。
            /// </summary>
            public void Dispose()
            {
                ResetPointerState();
                field.UnregisterValueChangedCallback(valueChangedCallback);
                field.UnregisterCallback(keyDownCallback, TrickleDown.TrickleDown);
                field.UnregisterCallback(focusOutCallback, TrickleDown.TrickleDown);
                field.UnregisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
                field.UnregisterCallback(pointerUpCallback, TrickleDown.TrickleDown);
                field.UnregisterCallback(pointerCaptureOutCallback, TrickleDown.TrickleDown);
            }
        }

        #endregion
    }
}
#endif
