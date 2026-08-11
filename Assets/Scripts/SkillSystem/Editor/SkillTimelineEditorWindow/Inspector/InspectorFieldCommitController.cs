#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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

        // 保存一次 Inspector 绘制周期的草稿取消入口。
        internal InspectorFieldCommitController(Action cancelDraft)
        {
            this.cancelDraft = cancelDraft;
        }

        #region 字段绑定

        // 为连续输入字段绑定草稿刷新、完成提交与 Escape 整体取消行为。
        internal void Bind<T>(BaseField<T> field, Action draftChanged, Action commit)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InspectorFieldCommitController));
            bindings.Add(new FieldBinding<T>(this, field, draftChanged, commit));
        }

        // 完成一个字段的编辑；提交前先清除 Dirty，避免同步 Inspector 刷新导致重复提交。
        private void Commit(IFieldBinding binding)
        {
            if (disposed || cancelling || !binding.IsDirty) return;
            binding.AcceptCurrentValue();
            binding.Commit();
        }

        // Escape 会恢复本轮全部字段的权威初值，并统一清除 Scene 预览草稿。
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
            private void OnValueChanged(ChangeEvent<T> _)
            {
                IsDirty = true;
                if (activePointerId != InvalidPointerId) changedDuringPointer = true;
                draftChanged?.Invoke();
            }

            // 开启一次 Pointer 编辑周期；已有键盘草稿不会因此被视为鼠标拖拽修改。
            private void OnPointerDown(PointerDownEvent evt)
            {
                if (activePointerId != InvalidPointerId) return;
                activePointerId = evt.pointerId;
                changedDuringPointer = false;
            }

            // 鼠标松开时仅提交本次 Pointer 周期实际改变过的字段值。
            private void OnPointerUp(PointerUpEvent evt)
            {
                if (activePointerId != evt.pointerId) return;
                CompletePointerEdit();
            }

            // Pointer Capture 丢失是拖出字段松手的兜底完成信号。
            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (activePointerId != evt.pointerId) return;
                CompletePointerEdit();
            }

            // 先重置 Pointer 状态再提交，避免同步 Inspector 重建时后续事件重复进入。
            private void CompletePointerEdit()
            {
                bool shouldCommit = changedDuringPointer;
                ResetPointerState();
                if (shouldCommit) owner.Commit(this);
            }

            // 清除当前 Pointer 编辑周期，不影响键盘输入产生的 Dirty 状态。
            private void ResetPointerState()
            {
                activePointerId = InvalidPointerId;
                changedDuringPointer = false;
            }

            // Enter 提交当前字段；Escape 恢复本轮全部字段且阻止 TextInput 继续处理按键。
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
