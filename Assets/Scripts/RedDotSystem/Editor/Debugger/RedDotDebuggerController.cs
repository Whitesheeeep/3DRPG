#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Game;
using UnityEditor;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>协调调试窗口生命周期、红点系统访问、Framework 事件及用户操作。</summary>
    internal sealed class RedDotDebuggerController : IDisposable
    {
        #region 依赖字段

        private readonly RedDotDebuggerView view;

        #endregion

        #region 状态字段

        private RedDotSystem redDotSystem;
        private IUnRegister changedEventUnregister;
        private bool connectQueued;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建 Controller、绑定 View 并监听 Play Mode 状态。</summary>
        /// <param name="view">红点调试窗口 View。</param>
        public RedDotDebuggerController(RedDotDebuggerView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            BindViewEvents();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            view.SetConnectionState(EditorApplication.isPlaying, false);
            if (EditorApplication.isPlaying)
            {
                QueueConnect();
            }
        }

        /// <summary>注销 Framework 事件、Play Mode 回调和全部 View 事件。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.delayCall -= ConnectInPlayMode;
            connectQueued = false;
            Disconnect();
            UnbindViewEvents();
        }

        #endregion

        #region 连接管理

        /// <summary>处理 Play Mode 状态并只在进入运行模式后安排连接。</summary>
        /// <param name="state">Unity 当前 Play Mode 状态。</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    QueueConnect();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall -= ConnectInPlayMode;
                    connectQueued = false;
                    Disconnect();
                    view.SetConnectionState(false, false);
                    break;
            }
        }

        /// <summary>把连接延迟到当前 Editor 回调结束，确保场景 Awake 已完成。</summary>
        private void QueueConnect()
        {
            if (connectQueued || disposed)
            {
                return;
            }

            connectQueued = true;
            EditorApplication.delayCall += ConnectInPlayMode;
            view.SetConnectionState(true, false);
        }

        /// <summary>在 Play Mode 中通过现有 GameArchitecture 接口取得红点系统。</summary>
        private void ConnectInPlayMode()
        {
            EditorApplication.delayCall -= ConnectInPlayMode;
            connectQueued = false;
            if (disposed || !EditorApplication.isPlaying)
            {
                return;
            }

            // 只有 Play Mode 分支才能访问 GameArchitecture.Interface，避免 Edit Mode 初始化架构。
            Disconnect();
            IArchitecture architecture = GameArchitecture.Interface;
            redDotSystem = architecture.GetSystem<RedDotSystem>();
            if (redDotSystem == null)
            {
                view.SetConnectionState(true, false);
                view.ShowStatus("GameArchitecture 尚未注册 RedDotSystem。", true);
                Debug.LogError("[RedDotDebugger] Play Mode 连接失败：GameArchitecture 未注册 RedDotSystem。");
                return;
            }

            changedEventUnregister = WSEventSystem.Register_Type<RedDotValueChangedEvent>(
                typeof(RedDotValueChangedEvent),
                HandleRedDotValueChanged);
            view.SetRuntimeConfigName(RedDotSystem.Config == null ? null : RedDotSystem.Config.name);
            view.SetConnectionState(true, true);
            RefreshSnapshot();
            view.ShowStatus("已连接 RedDotSystem。所有修改仅作用于当前 Play Mode。", false);
            Debug.Log("[RedDotDebugger] 已连接 RedDotSystem 并注册全局变化事件。");
        }

        /// <summary>释放全局事件句柄并清除运行时引用和界面数据。</summary>
        private void Disconnect()
        {
            bool wasConnected = redDotSystem != null || changedEventUnregister != null;
            changedEventUnregister?.UnRegister();
            changedEventUnregister = null;
            redDotSystem = null;
            view.SetRuntimeConfigName(null);
            view.ClearRuntimeData();
            if (wasConnected)
            {
                Debug.Log("[RedDotDebugger] 已断开 RedDotSystem 并注销全局变化事件。");
            }
        }

        #endregion

        #region 状态刷新

        /// <summary>响应 Framework 全局红点变化并读取最新完整快照。</summary>
        /// <param name="changedEvent">已经发生的节点变化。</param>
        private void HandleRedDotValueChanged(RedDotValueChangedEvent changedEvent)
        {
            if (redDotSystem == null)
            {
                return;
            }

            RefreshSnapshot();
            view.ShowStatus(
                $"{changedEvent.Key.DerivedPath}: {changedEvent.PreviousValue} → {changedEvent.CurrentValue}",
                false);
        }

        /// <summary>读取并渲染当前红点树快照，不触发节点计算。</summary>
        private void RefreshSnapshot()
        {
            if (redDotSystem == null)
            {
                return;
            }

            view.Render(redDotSystem.DebugGetSnapshot());
        }

        #endregion

        #region 用户操作

        /// <summary>立即刷新当前脏节点。</summary>
        private void OnFlushRequested() => ExecuteUserOperation(() =>
        {
            redDotSystem.DebugFlushNow();
            RefreshSnapshot();
        }, "已立即 Flush。");

        /// <summary>手动重新读取当前节点快照。</summary>
        private void OnRefreshRequested() => ExecuteUserOperation(RefreshSnapshot, "已刷新快照。");

        /// <summary>清除全部 Editor 临时覆盖。</summary>
        private void OnClearAllOverridesRequested() => ExecuteUserOperation(() =>
        {
            redDotSystem.DebugClearAllOverrides();
            RefreshSnapshot();
        }, "已清除全部 Override，等待帧末刷新。");

        /// <summary>在选中节点的有效自身值上应用差值。</summary>
        /// <param name="delta">待增加的差值。</param>
        private void OnDeltaRequested(int delta) => ExecuteSelectedNodeOperation(
            (key, selectedSnapshot) =>
            {
                int nextValue = checked(Math.Max(0, selectedSnapshot.EffectiveSelfValue + delta));
                redDotSystem.DebugSetSelfOverride(key, nextValue);
            },
            delta > 0 ? "已 +1，等待帧末刷新。" : "已 -1，等待帧末刷新。");

        /// <summary>把选中节点覆盖值设置为零。</summary>
        private void OnSetZeroRequested() => ExecuteSelectedNodeOperation(
            (key, selectedSnapshot) => redDotSystem.DebugSetSelfOverride(key, 0),
            "已设为 0，等待帧末刷新。");

        /// <summary>把选中节点覆盖值设置为输入值。</summary>
        /// <param name="value">用户输入值。</param>
        private void OnSetValueRequested(int value) => ExecuteSelectedNodeOperation(
            (key, selectedSnapshot) => redDotSystem.DebugSetSelfOverride(key, value),
            $"已设为 {value}，等待帧末刷新。");

        /// <summary>清除选中节点的临时覆盖。</summary>
        private void OnClearOverrideRequested() => ExecuteSelectedNodeOperation(
            (key, selectedSnapshot) => redDotSystem.DebugClearOverride(key),
            "已清除 Override，等待帧末刷新。");

        /// <summary>标记选中节点等待帧末重新计算。</summary>
        private void OnMarkDirtyRequested() => ExecuteSelectedNodeOperation(
            (key, selectedSnapshot) => redDotSystem.DebugMarkDirty(key),
            "已 Mark Dirty，等待帧末刷新。");

        /// <summary>复制当前选中节点的派生路径到系统剪贴板。</summary>
        private void OnCopyPathRequested()
        {
            RedDotDebuggerNodeViewData selectedNode = view.GetSelectedNode();
            if (selectedNode == null)
            {
                view.ShowStatus("请先选择一个节点。", true);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = selectedNode.Snapshot.DerivedPath;
            view.ShowStatus($"已复制：{selectedNode.Snapshot.DerivedPath}", false);
        }

        /// <summary>在 Project 窗口中定位选中的 RedDotKey Asset。</summary>
        private void OnPingKeyRequested()
        {
            RedDotDebuggerNodeViewData selectedNode = view.GetSelectedNode();
            if (selectedNode == null)
            {
                view.ShowStatus("请先选择一个节点。", true);
                return;
            }

            EditorGUIUtility.PingObject(selectedNode.Snapshot.Key);
            Selection.activeObject = selectedNode.Snapshot.Key;
            view.ShowStatus("已定位 RedDotKey Asset。", false);
        }

        #endregion

        #region 事件绑定与操作辅助

        /// <summary>订阅全部 View 用户意图事件。</summary>
        private void BindViewEvents()
        {
            view.FlushRequested += OnFlushRequested;
            view.RefreshRequested += OnRefreshRequested;
            view.ClearAllOverridesRequested += OnClearAllOverridesRequested;
            view.DeltaRequested += OnDeltaRequested;
            view.SetZeroRequested += OnSetZeroRequested;
            view.SetValueRequested += OnSetValueRequested;
            view.ClearOverrideRequested += OnClearOverrideRequested;
            view.MarkDirtyRequested += OnMarkDirtyRequested;
            view.CopyPathRequested += OnCopyPathRequested;
            view.PingKeyRequested += OnPingKeyRequested;
        }

        /// <summary>解除全部 View 用户意图事件。</summary>
        private void UnbindViewEvents()
        {
            view.FlushRequested -= OnFlushRequested;
            view.RefreshRequested -= OnRefreshRequested;
            view.ClearAllOverridesRequested -= OnClearAllOverridesRequested;
            view.DeltaRequested -= OnDeltaRequested;
            view.SetZeroRequested -= OnSetZeroRequested;
            view.SetValueRequested -= OnSetValueRequested;
            view.ClearOverrideRequested -= OnClearOverrideRequested;
            view.MarkDirtyRequested -= OnMarkDirtyRequested;
            view.CopyPathRequested -= OnCopyPathRequested;
            view.PingKeyRequested -= OnPingKeyRequested;
        }

        /// <summary>对当前选中节点执行调试操作并在出错时反馈到窗口。</summary>
        /// <param name="operation">接收选中节点 Asset 与最新快照的操作。</param>
        /// <param name="successMessage">操作成功提示。</param>
        private void ExecuteSelectedNodeOperation(
            Action<RedDotKey, DebugRedDotNodeSnapshot> operation,
            string successMessage)
        {
            RedDotDebuggerNodeViewData selectedNode = view.GetSelectedNode();
            if (selectedNode == null)
            {
                view.ShowStatus("请先选择一个节点。", true);
                return;
            }

            ExecuteUserOperation(
                () => operation(selectedNode.Snapshot.Key, selectedNode.Snapshot),
                successMessage);
        }

        /// <summary>在真实用户输入边界执行操作并把契约错误显示到窗口。</summary>
        /// <param name="operation">待执行窗口操作。</param>
        /// <param name="successMessage">操作成功提示。</param>
        private void ExecuteUserOperation(Action operation, string successMessage)
        {
            if (redDotSystem == null)
            {
                view.ShowStatus("RedDotSystem 未连接，仅可在 Play Mode 操作。", true);
                return;
            }

            try
            {
                operation();
                view.ShowStatus(successMessage, false);
            }
            catch (ArgumentException exception)
            {
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning($"[RedDotDebugger] 输入校验失败：{exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning($"[RedDotDebugger] 操作被拒绝：{exception.Message}");
            }
            catch (KeyNotFoundException exception)
            {
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning($"[RedDotDebugger] 节点已失效：{exception.Message}");
            }
            catch (OverflowException exception)
            {
                view.ShowStatus("数值超出 Int32 范围。", true);
                Debug.LogWarning($"[RedDotDebugger] 数值溢出：{exception.Message}");
            }
        }

        #endregion
    }
}
#endif
