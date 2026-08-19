#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SaveSystem.Editor
{
    /// <summary>
    /// 只读列举正式存档并查看 JSON Payload 的 UI Toolkit 编辑器窗口。
    /// </summary>
    public sealed class SaveJsonViewerWindow : EditorWindow
    {
        #region 窗口常量与状态

        private const string WindowTitle = "Save JSON 查看器";
        private const string MenuPath = "SaveSystem/JSON 存档查看器";
        private const string UxmlPath = "Assets/Scripts/SaveSystem/Editor/SaveJsonViewerWindow.uxml";
        private const string UssPath = "Assets/Scripts/SaveSystem/Editor/SaveJsonViewerWindow.uss";
        private const int JsonReadBufferSize = 4096;

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly List<SaveStorageEntry> entries = new List<SaveStorageEntry>();
        private ListView slotList;
        private Button refreshButton;
        private Button readButton;
        private Label statusLabel;
        private Label metadataLabel;
        private TextField jsonText;
        private CancellationTokenSource operationCancellation;
        private int operationGeneration;
        private bool windowReady;
        private bool hasSelectedEntry;
        private SaveStorageEntry selectedEntry;

        #endregion

        #region 窗口生命周期

        /// <summary>
        /// 打开或聚焦 JSON 存档查看器窗口。
        /// </summary>
        [MenuItem(MenuPath)]
        private static void OpenWindow()
        {
            SaveJsonViewerWindow window = GetWindow<SaveJsonViewerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(920f, 560f);
            window.Show();
            window.Focus();
        }

        /// <summary>
        /// 创建 UI Toolkit 视图并在窗口首次显示时刷新正式存档列表。
        /// </summary>
        private void CreateGUI()
        {
            DisposeUi();
            rootVisualElement.Clear();

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                rootVisualElement.Add(new HelpBox($"缺少窗口 UXML：{UxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
            else
            {
                rootVisualElement.Add(new HelpBox($"缺少窗口 USS：{UssPath}", HelpBoxMessageType.Warning));
            }

            QueryElements();
            ConfigureListView();
            RegisterEvents();
            windowReady = true;
            RefreshSelection();
            RefreshEntriesAsync().Forget();
        }

        /// <summary>
        /// 窗口禁用时取消读取并解除 UI 事件，避免句柄和异步操作跨窗口生命周期存活。
        /// </summary>
        private void OnDisable()
        {
            DisposeUi();
        }

        /// <summary>
        /// 窗口销毁时再次执行幂等释放，确保底层文件流不会遗留。
        /// </summary>
        private void OnDestroy()
        {
            DisposeUi();
        }

        /// <summary>
        /// 查询 UXML 中的固定控件，并配置 JSON 文本框为只读多行模式。
        /// </summary>
        private void QueryElements()
        {
            slotList = rootVisualElement.Q<ListView>("SlotList");
            refreshButton = rootVisualElement.Q<Button>("RefreshButton");
            readButton = rootVisualElement.Q<Button>("ReadButton");
            statusLabel = rootVisualElement.Q<Label>("StatusLabel");
            metadataLabel = rootVisualElement.Q<Label>("MetadataLabel");
            jsonText = rootVisualElement.Q<TextField>("JsonText");
            jsonText.multiline = true;
            jsonText.isReadOnly = true;
        }

        /// <summary>
        /// 配置槽位列表的行创建、数据绑定和单选行为。
        /// </summary>
        private void ConfigureListView()
        {
            slotList.fixedItemHeight = 70f;
            slotList.selectionType = SelectionType.Single;
            slotList.itemsSource = entries;
            slotList.makeItem = MakeSaveRow;
            slotList.bindItem = BindSaveRow;
        }

        /// <summary>
        /// 注册窗口按钮和槽位列表事件。
        /// </summary>
        private void RegisterEvents()
        {
            refreshButton.clicked += OnRefreshClicked;
            readButton.clicked += OnReadClicked;
            slotList.selectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 对称解除窗口控件事件，避免 UI 重建后重复触发旧回调。
        /// </summary>
        private void UnregisterEvents()
        {
            if (refreshButton != null)
            {
                refreshButton.clicked -= OnRefreshClicked;
            }

            if (readButton != null)
            {
                readButton.clicked -= OnReadClicked;
            }

            if (slotList != null)
            {
                slotList.selectionChanged -= OnSelectionChanged;
            }
        }

        /// <summary>
        /// 取消当前文件操作、递增操作代次并解除 UI 引用。
        /// </summary>
        private void DisposeUi()
        {
            windowReady = false;
            CancelOperation();
            UnregisterEvents();
            slotList = null;
            refreshButton = null;
            readButton = null;
            statusLabel = null;
            metadataLabel = null;
            jsonText = null;
            hasSelectedEntry = false;
            selectedEntry = default;
        }

        #endregion

        #region UI 操作

        /// <summary>
        /// 响应刷新按钮，重新枚举正式存档但不读取业务 Payload。
        /// </summary>
        private void OnRefreshClicked()
        {
            RefreshEntriesAsync().Forget();
        }

        /// <summary>
        /// 响应读取按钮，打开当前选中的 JSON 槽位并显示 Payload。
        /// </summary>
        private void OnReadClicked()
        {
            ReadSelectedPayloadAsync().Forget();
        }

        /// <summary>
        /// 接收列表选择结果并刷新右侧槽位元数据与读取按钮状态。
        /// </summary>
        /// <param name="selection">当前列表选中的槽位条目集合。</param>
        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            hasSelectedEntry = false;
            if (selection != null)
            {
                foreach (object item in selection)
                {
                    if (item is SaveStorageEntry entry)
                    {
                        selectedEntry = entry;
                        hasSelectedEntry = true;
                        break;
                    }
                }
            }

            RefreshSelection();
        }

        /// <summary>
        /// 将当前槽位选择投影到元数据显示区，并只允许读取有效 JSON 条目。
        /// </summary>
        private void RefreshSelection()
        {
            if (!windowReady || metadataLabel == null)
            {
                return;
            }

            if (!hasSelectedEntry)
            {
                metadataLabel.text = "未选择存档";
                SetReadButtonState();
                return;
            }

            metadataLabel.text = FormatEntryMetadata(selectedEntry);
            if (!selectedEntry.IsAvailable)
            {
                SetStatus($"槽位不可读取：error={selectedEntry.ErrorCode}，{selectedEntry.Message}");
            }
            else if (!string.Equals(
                         selectedEntry.FormatId,
                         NewtonsoftJsonSaveSerializer.JsonFormatId,
                         StringComparison.Ordinal))
            {
                SetStatus(
                    $"无法按 JSON 查看：FormatId={selectedEntry.FormatId}，error={SaveErrorCode.UnknownSerializerFormat}");
            }
            else
            {
                SetStatus($"已选择 JSON 槽位：{selectedEntry.SlotId}，点击“读取 JSON”查看 Payload。");
            }
            SetReadButtonState();
        }

        /// <summary>
        /// 设置刷新和读取按钮，防止同一窗口同时启动多个文件操作。
        /// </summary>
        /// <param name="isBusy">当前是否有异步操作运行。</param>
        private void SetOperationControls(bool isBusy)
        {
            if (!windowReady)
            {
                return;
            }

            refreshButton?.SetEnabled(!isBusy);
            slotList?.SetEnabled(!isBusy);
            SetReadButtonState(isBusy);
        }

        /// <summary>
        /// 根据当前选中条目和运行状态设置读取按钮是否可用。
        /// </summary>
        /// <param name="isBusy">可选的忙碌状态；未提供时读取当前操作状态。</param>
        private void SetReadButtonState(bool? isBusy = null)
        {
            if (readButton == null)
            {
                return;
            }

            bool operationRunning = isBusy ?? operationCancellation != null;
            bool canRead = hasSelectedEntry &&
                           selectedEntry.IsAvailable &&
                           string.Equals(
                               selectedEntry.FormatId,
                               NewtonsoftJsonSaveSerializer.JsonFormatId,
                               StringComparison.Ordinal);
            readButton.SetEnabled(!operationRunning && canRead);
        }

        /// <summary>
        /// 更新窗口状态文本并保留统一的查看器前缀。
        /// </summary>
        /// <param name="message">需要显示的状态文本。</param>
        private void SetStatus(string message)
        {
            if (windowReady && statusLabel != null)
            {
                statusLabel.text = message ?? string.Empty;
            }
        }

        /// <summary>
        /// 清空上一次读取的 JSON，避免槽位切换时误看旧内容。
        /// </summary>
        private void ClearPayload()
        {
            if (jsonText != null)
            {
                jsonText.value = string.Empty;
            }
        }

        #endregion

        #region 存档读取操作

        /// <summary>
        /// 枚举正式存档目录并刷新列表，不反序列化业务 Payload。
        /// </summary>
        private async UniTask RefreshEntriesAsync()
        {
            if (!TryBeginOperation("刷新存档列表", out CancellationToken cancellationToken, out int generation))
            {
                return;
            }

            try
            {
                SetStatus("正在列出正式存档……");
                LocalFileSaveStorage storage = CreateStorage();
                SaveResult<IReadOnlyList<SaveStorageEntry>> result =
                    await storage.ListEntriesAsync(cancellationToken);
                if (!IsCurrentOperation(generation))
                {
                    return;
                }

                if (!result.IsSuccess)
                {
                    ShowFailure(generation, result.ErrorCode, result.Message, result.Exception);
                    return;
                }

                entries.Clear();
                for (int index = 0; index < result.Value.Count; index++)
                {
                    entries.Add(result.Value[index]);
                }

                hasSelectedEntry = false;
                selectedEntry = default;
                slotList.Rebuild();
                slotList.ClearSelection();
                ClearPayload();
                RefreshSelection();
                SetStatus($"已列出 {entries.Count} 个正式存档：{SaveDirectoryPath}");
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentOperation(generation))
                {
                    SetStatus("存档列表读取已取消。");
                }
            }
            catch (Exception exception)
            {
                ShowFailure(generation, SaveErrorCode.StorageReadFailed, "列出正式存档失败。", exception);
            }
            finally
            {
                EndOperation(generation);
            }
        }

        /// <summary>
        /// 读取选中槽位的限定 Payload Stream，并将其校验后显示为缩进 JSON。
        /// </summary>
        private async UniTask ReadSelectedPayloadAsync()
        {
            if (!hasSelectedEntry)
            {
                SetStatus("请先选择一个存档槽位。");
                return;
            }

            if (!selectedEntry.IsAvailable)
            {
                SetStatus($"无法读取损坏槽位：{selectedEntry.ErrorCode}，{selectedEntry.Message}");
                return;
            }

            if (!string.Equals(
                    selectedEntry.FormatId,
                    NewtonsoftJsonSaveSerializer.JsonFormatId,
                    StringComparison.Ordinal))
            {
                SetStatus(
                    $"无法按 JSON 查看：FormatId={selectedEntry.FormatId}，错误码={SaveErrorCode.UnknownSerializerFormat}");
                return;
            }

            if (!TryBeginOperation("读取 JSON Payload", out CancellationToken cancellationToken, out int generation))
            {
                return;
            }

            try
            {
                SetStatus($"正在读取槽位 {selectedEntry.SlotId} 的 JSON Payload……");
                LocalFileSaveStorage storage = CreateStorage();
                SaveResult<ISaveReadHandle> openResult =
                    await storage.OpenReadAsync(selectedEntry.SlotId, cancellationToken);
                if (!IsCurrentOperation(generation))
                {
                    return;
                }

                if (!openResult.IsSuccess)
                {
                    ShowFailure(generation, openResult.ErrorCode, openResult.Message, openResult.Exception);
                    return;
                }

                string rawJson;
                using (ISaveReadHandle handle = openResult.Value)
                {
                    // Handle 的释放必须包住整个读取过程，确保文件句柄和受限 Payload Stream 一起关闭。
                    if (!string.Equals(
                            handle.FormatId,
                            NewtonsoftJsonSaveSerializer.JsonFormatId,
                            StringComparison.Ordinal))
                    {
                        ShowFailure(
                            generation,
                            SaveErrorCode.UnknownSerializerFormat,
                            $"容器头 FormatId={handle.FormatId}，不是 JSON。",
                            null);
                        return;
                    }

                    rawJson = await ReadPayloadTextAsync(handle.Content, cancellationToken);
                }

                string formattedJson = FormatJsonPayload(rawJson);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentOperation(generation))
                {
                    return;
                }

                jsonText.value = formattedJson;
                SetStatus(
                    $"读取成功：slot={selectedEntry.SlotId}，Payload={selectedEntry.PayloadLength} bytes，JSON={formattedJson.Length} chars");
                Debug.Log(
                    $"[SaveJsonViewer] JSON Payload 读取成功：slot={selectedEntry.SlotId}, " +
                    $"path={GetSlotPath(selectedEntry.SlotId)}, payload={selectedEntry.PayloadLength} bytes",
                    this);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentOperation(generation))
                {
                    SetStatus("JSON Payload 读取已取消。");
                }
            }
            catch (DecoderFallbackException exception)
            {
                ShowFailure(generation, SaveErrorCode.DeserializationFailed, "JSON Payload 不是有效 UTF-8。", exception);
            }
            catch (JsonException exception)
            {
                ShowFailure(generation, SaveErrorCode.DeserializationFailed, "JSON Payload 格式无效。", exception);
            }
            catch (InvalidDataException exception)
            {
                ShowFailure(generation, SaveErrorCode.DeserializationFailed, exception.Message, exception);
            }
            catch (IOException exception)
            {
                ShowFailure(generation, SaveErrorCode.StorageReadFailed, "读取 JSON Payload 失败。", exception);
            }
            catch (Exception exception)
            {
                ShowFailure(generation, SaveErrorCode.Unknown, "读取 JSON Payload 时发生未分类错误。", exception);
            }
            finally
            {
                EndOperation(generation);
            }
        }

        /// <summary>
        /// 从受限 Payload Stream 读取严格 UTF-8 文本，同时保持调用方句柄的所有权。
        /// </summary>
        /// <param name="source">已经完成容器校验的 Payload Stream。</param>
        /// <param name="cancellationToken">取消文本读取的令牌。</param>
        /// <returns>原始 JSON 文本。</returns>
        private static async UniTask<string> ReadPayloadTextAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            using (var reader = new StreamReader(
                       source,
                       StrictUtf8,
                       false,
                       JsonReadBufferSize,
                       true))
            {
                string text = await reader.ReadToEndAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length > 0 && text[0] == '\uFEFF')
                {
                    throw new InvalidDataException("JSON Payload 不应包含 UTF-8 BOM。");
                }

                return text;
            }
        }

        /// <summary>
        /// 验证单一 JSON 根值并输出缩进格式，不执行业务模块类型恢复。
        /// </summary>
        /// <param name="rawJson">待读取的原始 JSON 文本。</param>
        /// <returns>用于编辑器只读展示的缩进 JSON。</returns>
        private static string FormatJsonPayload(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new JsonReaderException("JSON Payload 为空。");
            }

            using (var stringReader = new StringReader(rawJson))
            using (var jsonReader = new JsonTextReader(stringReader)
                   {
                       CloseInput = false,
                       DateParseHandling = DateParseHandling.None
                   })
            {
                JToken root = JToken.Load(jsonReader);
                // 读取根节点后的下一个 Token，拒绝第二个 JSON 值或尾部非空内容。
                if (jsonReader.Read())
                {
                    throw new JsonReaderException("JSON Payload 根对象之后包含额外内容。");
                }

                return root.ToString(Formatting.Indented);
            }
        }

        #endregion

        #region 操作生命周期与错误

        /// <summary>
        /// 为一次异步操作创建取消令牌，并阻止窗口内的并发读取。
        /// </summary>
        /// <param name="operationName">日志中的操作名称。</param>
        /// <param name="cancellationToken">成功开始操作时返回的取消令牌。</param>
        /// <param name="generation">用于忽略旧操作回调的操作代次。</param>
        /// <returns>没有其他操作运行且窗口有效时返回 true。</returns>
        private bool TryBeginOperation(
            string operationName,
            out CancellationToken cancellationToken,
            out int generation)
        {
            cancellationToken = default;
            generation = operationGeneration;
            if (!windowReady || operationCancellation != null)
            {
                Debug.LogWarning($"[SaveJsonViewer] 已有操作运行中或窗口未就绪，忽略请求：{operationName}", this);
                return false;
            }

            operationCancellation = new CancellationTokenSource();
            generation = ++operationGeneration;
            cancellationToken = operationCancellation.Token;
            SetOperationControls(true);
            return true;
        }

        /// <summary>
        /// 判断异步操作是否仍属于当前窗口和当前操作代次。
        /// </summary>
        /// <param name="generation">异步操作启动时记录的代次。</param>
        /// <returns>操作仍可更新 UI 时返回 true。</returns>
        private bool IsCurrentOperation(int generation) =>
            windowReady && operationCancellation != null && operationGeneration == generation;

        /// <summary>
        /// 结束当前操作并恢复按钮状态；旧操作不会影响新操作。
        /// </summary>
        /// <param name="generation">结束操作的代次。</param>
        private void EndOperation(int generation)
        {
            if (operationGeneration != generation)
            {
                return;
            }

            operationCancellation?.Dispose();
            operationCancellation = null;
            SetOperationControls(false);
        }

        /// <summary>
        /// 取消当前异步操作并使其后续回调失效。
        /// </summary>
        private void CancelOperation()
        {
            operationGeneration++;
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
        }

        /// <summary>
        /// 把存档边界错误显示到窗口并写入 Unity Console 供诊断。
        /// </summary>
        /// <param name="generation">产生错误的操作代次。</param>
        /// <param name="errorCode">结构化存档错误码。</param>
        /// <param name="message">错误诊断消息。</param>
        /// <param name="exception">可选原始异常。</param>
        private void ShowFailure(
            int generation,
            SaveErrorCode errorCode,
            string message,
            Exception exception)
        {
            if (!IsCurrentOperation(generation))
            {
                return;
            }

            ClearPayload();
            string text = $"读取失败：error={errorCode}，{message}";
            SetStatus(text);
            Debug.LogError($"[SaveJsonViewer] {text}", this);
            if (exception != null)
            {
                Debug.LogException(exception, this);
            }
        }

        #endregion

        #region 存档显示辅助

        /// <summary>
        /// 创建指向正式持久化目录的本地存储实例。
        /// </summary>
        /// <returns>正式 Saves 目录存储器。</returns>
        private static LocalFileSaveStorage CreateStorage() =>
            new LocalFileSaveStorage(SaveDirectoryPath);

        /// <summary>
        /// 获取正式存档目录的绝对路径。
        /// </summary>
        private static string SaveDirectoryPath =>
            Path.Combine(Application.persistentDataPath, SaveStorageDefaults.LocalDirectoryName);

        /// <summary>
        /// 获取指定槽位的正式文件路径，仅用于诊断日志。
        /// </summary>
        /// <param name="slotId">目标槽位标识。</param>
        /// <returns>标准 .save 文件路径。</returns>
        private static string GetSlotPath(SaveSlotId slotId) =>
            Path.Combine(SaveDirectoryPath, SaveContainerFormat.GetFileName(slotId));

        /// <summary>
        /// 创建槽位列表中的动态行控件。
        /// </summary>
        /// <returns>用于绑定 SaveStorageEntry 的列表行。</returns>
        private static VisualElement MakeSaveRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("save-row");

            VisualElement textBlock = new VisualElement();
            textBlock.AddToClassList("save-row-text");
            Label slotIdLabel = new Label { name = "SlotId" };
            slotIdLabel.AddToClassList("save-row-title");
            textBlock.Add(slotIdLabel);
            Label detailsLabel = new Label { name = "Details" };
            detailsLabel.AddToClassList("save-row-details");
            textBlock.Add(detailsLabel);
            row.Add(textBlock);

            Label state = new Label { name = "State" };
            state.AddToClassList("save-row-state");
            row.Add(state);
            return row;
        }

        /// <summary>
        /// 将槽位元数据绑定到动态列表行，不读取业务 Payload。
        /// </summary>
        /// <param name="element">待绑定的行元素。</param>
        /// <param name="index">列表数据索引。</param>
        private void BindSaveRow(VisualElement element, int index)
        {
            SaveStorageEntry entry = entries[index];
            element.Q<Label>("SlotId").text = entry.SlotId.ToString();
            element.Q<Label>("Details").text =
                $"Format={FormatOrDash(entry.FormatId)}  Payload={entry.PayloadLength} bytes";

            Label state = element.Q<Label>("State");
            state.text = entry.IsAvailable ? "可用" : $"损坏\n{entry.ErrorCode}";
            state.EnableInClassList("is-available", entry.IsAvailable);
            state.EnableInClassList("is-corrupted", !entry.IsAvailable);
        }

        /// <summary>
        /// 格式化右侧元数据，包含容器状态和损坏诊断。
        /// </summary>
        /// <param name="entry">当前选中的存档条目。</param>
        /// <returns>元数据显示文本。</returns>
        private static string FormatEntryMetadata(SaveStorageEntry entry)
        {
            string text =
                $"SlotId: {entry.SlotId}\n" +
                $"State: {(entry.IsAvailable ? "Available" : "Corrupted")}\n" +
                $"FormatId: {FormatOrDash(entry.FormatId)}\n" +
                $"ContainerVersion: {entry.ContainerVersion}\n" +
                $"PayloadLength: {entry.PayloadLength} bytes";
            if (!entry.IsAvailable)
            {
                text += $"\nError: {entry.ErrorCode}\nMessage: {entry.Message}";
            }

            return text;
        }

        /// <summary>
        /// 将空格式标识显示为短横线，避免列表布局出现空白歧义。
        /// </summary>
        /// <param name="formatId">容器格式标识。</param>
        /// <returns>可读格式标识。</returns>
        private static string FormatOrDash(string formatId) =>
            string.IsNullOrEmpty(formatId) ? "-" : formatId;

        #endregion
    }
}
#endif
