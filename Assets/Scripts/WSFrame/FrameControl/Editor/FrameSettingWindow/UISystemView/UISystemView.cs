using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule;

namespace WS_Modules
{
    internal sealed class UISystemView
    {
        private const string ViewUxmlPath =
            "Assets/Scripts/WSFrame/FrameControl/Editor/FrameSettingWindow/UISystemView/UISystemView.uxml";
        private const string UIManagerSettingPropertyPath = "uiManagerSetting";
        private const string HiddenClassName = "is-hidden";

        private VisualElement uiManagerSettingHost;
        private Label topWindowLabel;
        private Button refreshButton;
        private Label runtimeStateMessage;
        private VisualElement runtimeTable;
        private VisualElement runtimeTableBody;

        public void Draw(VisualElement container, WSFrameSetting frameSetting)
        {
            if (frameSetting == null)
            {
                container.Add(new HelpBox("FrameSetting is missing.", HelpBoxMessageType.Warning));
                return;
            }

            VisualTreeAsset viewAsset = LoadViewAsset();
            if (viewAsset == null)
            {
                container.Add(new HelpBox($"Missing UXML: {ViewUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            VisualElement root = viewAsset.CloneTree();
            container.Add(root);

            CacheElements(root);
            DrawUIManagerSetting(frameSetting);
            BindRuntimeEvents();
            RefreshRuntimeWindowInfo();
        }

        private static VisualTreeAsset LoadViewAsset()
        {
            VisualTreeAsset viewAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ViewUxmlPath);
            if (viewAsset != null)
            {
                return viewAsset;
            }

            string[] guids = AssetDatabase.FindAssets("UISystemView t:VisualTreeAsset");
            if (guids.Length == 0)
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }

        private void CacheElements(VisualElement root)
        {
            uiManagerSettingHost = root.Q<VisualElement>("UIManagerSettingHost");
            topWindowLabel = root.Q<Label>("RuntimeTopWindowLabel");
            refreshButton = root.Q<Button>("RuntimeRefreshButton");
            runtimeStateMessage = root.Q<Label>("RuntimeStateMessage");
            runtimeTable = root.Q<VisualElement>("RuntimeTable");
            runtimeTableBody = root.Q<VisualElement>("RuntimeTableBody");
        }

        private void DrawUIManagerSetting(WSFrameSetting frameSetting)
        {
            if (uiManagerSettingHost == null)
            {
                return;
            }

            uiManagerSettingHost.Clear();
            SerializedObject serializedObject = new SerializedObject(frameSetting);
            SerializedProperty uiManagerSettingProperty = serializedObject.FindProperty(UIManagerSettingPropertyPath);
            if (uiManagerSettingProperty == null)
            {
                uiManagerSettingHost.Add(new HelpBox(
                    $"Property '{UIManagerSettingPropertyPath}' not found.",
                    HelpBoxMessageType.Error));
                return;
            }

            var imguiContainer = new IMGUIContainer(() =>
            {
                if (frameSetting == null)
                {
                    return;
                }

                serializedObject.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(uiManagerSettingProperty, true);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(frameSetting, "Modify UIManager Settings");
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(frameSetting);
                }
            });
            uiManagerSettingHost.Add(imguiContainer);
        }

        private void BindRuntimeEvents()
        {
            if (refreshButton != null)
            {
                refreshButton.clicked += RefreshRuntimeWindowInfo;
            }
        }

        private void RefreshRuntimeWindowInfo()
        {
            runtimeTableBody?.Clear();

            if (topWindowLabel == null || runtimeStateMessage == null || runtimeTable == null || runtimeTableBody == null)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                SetRuntimeMessage("运行时窗口信息仅在 Play Mode 下可用。");
                SetTopWindowName(string.Empty);
                SetTableVisible(false);
                return;
            }

            if (!UIManager.Instance.IsInitialized)
            {
                SetRuntimeMessage("UIManager 尚未初始化，暂无运行时窗口信息。");
                SetTopWindowName(string.Empty);
                SetTableVisible(false);
                return;
            }

            bool hasTopWindow = UIManager.Instance.TryGetTopWindowSnapshot(out UIWindowSnapshot topWindowSnapshot);
            string topWindowName = hasTopWindow ? topWindowSnapshot.WindowName : string.Empty;
            SetTopWindowName(topWindowName);

            IReadOnlyList<UIWindowSnapshot> snapshots = UIManager.Instance.GetWindowSnapshots();
            if (snapshots.Count == 0)
            {
                SetRuntimeMessage("当前没有已加载窗口。");
                SetTableVisible(false);
                return;
            }

            SetRuntimeMessage(string.Empty);
            SetTableVisible(true);
            foreach (UIWindowSnapshot snapshot in snapshots)
            {
                runtimeTableBody.Add(CreateSnapshotTableRow(snapshot, topWindowName, RefreshRuntimeWindowInfo));
            }
        }

        private void SetTopWindowName(string topWindowName)
        {
            topWindowLabel.text = string.IsNullOrEmpty(topWindowName)
                ? "TopWindow: None"
                : $"TopWindow: {topWindowName}";
        }

        private void SetRuntimeMessage(string message)
        {
            runtimeStateMessage.text = message;
            runtimeStateMessage.EnableInClassList(HiddenClassName, string.IsNullOrEmpty(message));
        }

        private void SetTableVisible(bool visible)
        {
            runtimeTable.EnableInClassList(HiddenClassName, !visible);
        }

        private static VisualElement CreateSnapshotTableRow(
            UIWindowSnapshot snapshot,
            string topWindowName,
            Action refreshCallback)
        {
            var row = new VisualElement();
            row.AddToClassList("ui-runtime-row");
            row.EnableInClassList("is-top-window", snapshot.WindowName == topWindowName);

            row.Add(CreateTableCell(snapshot.WindowName == topWindowName
                ? $"{snapshot.WindowName} (Top)"
                : snapshot.WindowName, "col-window-name"));
            row.Add(CreateStateCell(snapshot.State));
            row.Add(CreateTableCell(snapshot.Visible.ToString(), "col-visible", GetBooleanClassName(snapshot.Visible)));
            row.Add(CreateTableCell(snapshot.SortingOrder.ToString(), "col-sorting-order"));
            row.Add(CreateTableCell(snapshot.SiblingIndex.ToString(), "col-sibling-index"));
            row.Add(CreateTableCell(snapshot.FullScreenWindow.ToString(), "col-full-screen", GetBooleanClassName(snapshot.FullScreenWindow)));
            row.Add(CreateTableCell(snapshot.HasMask.ToString(), "col-has-mask", GetBooleanClassName(snapshot.HasMask)));
            row.Add(CreateTableCell(snapshot.HasGameObject.ToString(), "col-has-game-object", GetBooleanClassName(snapshot.HasGameObject)));
            row.Add(CreateActionCell(snapshot, refreshCallback));
            return row;
        }

        private static VisualElement CreateActionCell(UIWindowSnapshot snapshot, Action refreshCallback)
        {
            var cell = new VisualElement();
            cell.AddToClassList("ui-runtime-cell");
            cell.AddToClassList("col-actions");
            cell.AddToClassList("ui-runtime-actions");

            var popUpButton = new Button(() =>
            {
                PopUpWindow(snapshot.WindowName);
                RefreshAfterRuntimeAction(refreshCallback);
            })
            {
                text = "PopUp"
            };
            popUpButton.AddToClassList("ui-runtime-action-button");

            var hideButton = new Button(() =>
            {
                UIManager.Instance.HideWindow(snapshot.WindowName);
                RefreshAfterRuntimeAction(refreshCallback);
            })
            {
                text = "Hide"
            };
            hideButton.AddToClassList("ui-runtime-action-button");

            cell.Add(popUpButton);
            cell.Add(hideButton);
            return cell;
        }

        private static void RefreshAfterRuntimeAction(Action refreshCallback)
        {
            refreshCallback?.Invoke();
            EditorApplication.delayCall += () => refreshCallback?.Invoke();
        }

        private static void PopUpWindow(string windowName)
        {
            Type windowType = FindWindowType(windowName);
            if (windowType == null)
            {
                Debug.LogWarning($"[UISystemView] 未找到窗口类型，无法手动弹出窗口：{windowName}");
                return;
            }

            MethodInfo method = FindGenericPopUpWindowMethod();
            if (method == null)
            {
                Debug.LogWarning("[UISystemView] 未找到 UIManager.PopUpWindow<T>() 方法。");
                return;
            }

            try
            {
                method.MakeGenericMethod(windowType).Invoke(UIManager.Instance, null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UISystemView] 手动弹出窗口失败：{windowName}\n{exception}");
            }
        }

        private static Type FindWindowType(string windowName)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType($"WS_Modules.UIModule.{windowName}") ?? assembly.GetType(windowName);
                if (type != null && typeof(WindowBase).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            return null;
        }

        private static MethodInfo FindGenericPopUpWindowMethod()
        {
            MethodInfo[] methods = typeof(UIManager).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (MethodInfo method in methods)
            {
                if (method.Name != nameof(UIManager.PopUpWindow) ||
                    !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1 ||
                    method.GetParameters().Length != 0)
                {
                    continue;
                }

                return method;
            }

            return null;
        }

        private static VisualElement CreateStateCell(UIWindowState state)
        {
            var cell = new VisualElement();
            cell.AddToClassList("ui-runtime-cell");
            cell.AddToClassList("col-state");

            var badge = new Label(state.ToString());
            badge.AddToClassList("ui-state-badge");
            badge.AddToClassList(GetStateClassName(state));
            cell.Add(badge);
            return cell;
        }

        private static Label CreateTableCell(string text, string columnClassName, string valueClassName = null)
        {
            var cell = new Label(text);
            cell.AddToClassList("ui-runtime-cell");
            cell.AddToClassList(columnClassName);
            if (!string.IsNullOrEmpty(valueClassName))
            {
                cell.AddToClassList(valueClassName);
            }

            return cell;
        }

        private static string GetStateClassName(UIWindowState state)
        {
            return state switch
            {
                UIWindowState.Loading => "state-loading",
                UIWindowState.Hidden => "state-hidden",
                UIWindowState.Showing => "state-showing",
                UIWindowState.Visible => "state-visible",
                UIWindowState.Hiding => "state-hiding",
                _ => "state-unknown",
            };
        }

        private static string GetBooleanClassName(bool value)
        {
            return value ? "value-true" : "value-false";
        }
    }
}
