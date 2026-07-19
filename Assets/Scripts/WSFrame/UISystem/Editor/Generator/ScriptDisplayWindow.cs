using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 脚本展示与生成窗口
    /// </summary>
    public class ScriptDisplayWindow : EditorWindow
    {
        /// <summary>
        /// 脚本展示窗口中的待生成文件。
        /// </summary>
        public readonly struct GeneratedScriptFile
        {
            /// <summary>
            /// 创建待生成文件数据。
            /// </summary>
            /// <param name="content">脚本内容。</param>
            /// <param name="filePath">脚本路径。</param>
            /// <param name="overwriteExisting">文件存在时是否整体覆盖。</param>
            public GeneratedScriptFile(string content, string filePath, bool overwriteExisting)
            {
                Content = content;
                FilePath = filePath;
                OverwriteExisting = overwriteExisting;
            }

            /// <summary>
            /// 脚本内容。
            /// </summary>
            public string Content { get; }

            /// <summary>
            /// 脚本路径。
            /// </summary>
            public string FilePath { get; }

            /// <summary>
            /// 文件存在时是否整体覆盖。
            /// </summary>
            public bool OverwriteExisting { get; }
        }

        private sealed class ScriptPreviewFile
        {
            public string Content;
            public string FilePath;
            public string FileName;
            public bool OverwriteExisting;
        }

        private string scriptContent; // 脚本内容
        private string filePath; // 文件路径
        private string mFileName; // 文件名
        private Vector2 scroll = new Vector2(); // 滚动视图位置
        private readonly List<ScriptPreviewFile> scriptFiles = new List<ScriptPreviewFile>();
        private int selectedFileIndex;
        private Action onBeforeGenerate;

        /// <summary>
        /// 显示代码展示窗口
        /// </summary>
        /// <param name="content">要显示的脚本内容</param>
        /// <param name="filePath">脚本文件路径</param>
        /// <param name="_insertDic">需要插入的方法字典 (方法名 -> 方法体)</param>
        /// <param name="fieldList">需要插入的字段列表</param>
        /// <param name="isBindData">是否为 BindData 脚本（决定事件插入位置）</param>
        public static void ShowWindow(
            string content,
            string filePath,
            Dictionary<string, string> _insertDic = null,
            List<EditorObjectData> fieldList = null,
            bool isBindData = false,
            List<GeneratedScriptFile> extraFiles = null,
            Action onBeforeGenerate = null)
        {
            //创建代码展示窗口
            ScriptDisplayWindow window = (ScriptDisplayWindow)GetWindowWithRect(typeof(ScriptDisplayWindow), new Rect(100, 50, 800, 700), true, "Window生成界面");
            window.scriptContent = content;
            window.filePath = filePath;
            window.mFileName = Path.GetFileName(filePath);
            window.selectedFileIndex = 0;
            window.onBeforeGenerate = onBeforeGenerate;
            window.scriptFiles.Clear();
            //处理代码新增
            string originScript = string.Empty;
            bool isInsterSuccess = false;

            // DataComponent 是纯自动绑定层，允许重新生成时整体覆盖。
            // WindowCode 和 Item 脚本才进入下方的保留旧代码追加逻辑。
            if (isBindData)
            {
                window.SetPreviewFiles(content, filePath, true, extraFiles);
                window.Show();
                return;
            }

            // 如果文件已存在，并且有需要插入的内容，则进行代码注入
            if (File.Exists(window.filePath) && (_insertDic != null || fieldList != null))
            {
                originScript = File.ReadAllText(window.filePath);

                if (!string.IsNullOrEmpty(originScript))
                {
                    if (fieldList != null)
                    {
                        //插入字段(生成item脚本时使用)
                        foreach (var item in fieldList)
                        {
                            // 避免重复插入
                            if (!originScript.Contains($"{item.fieldName}{item.fieldType}"))
                            {
                                string insterArrayType = item.dataList != null ? "[]" : "";
                                string insterArray = item.dataList != null ? "Array" : "";
                                int insertIndex = window.GetInsertFieldIndex(originScript);
                                if (insertIndex < 0)
                                {
                                    Debug.LogWarning($"未找到字段插入位置，已跳过字段：{item.fieldName}{item.fieldType}");
                                    continue;
                                }
                                // 插入新增的字段
                                originScript = window.scriptContent = originScript.Insert(insertIndex,
                                    $"\n\t\tpublic {item.fieldType}{insterArrayType} {item.fieldName}{item.fieldType}{insterArray};\n\t\t");
                                isInsterSuccess = true;
                            }
                        }
                    }
                    if (_insertDic != null)
                    {
                        //插入方法
                        foreach (var item in _insertDic)
                        {
                            // 避免重复插入
                            if (!originScript.Contains(item.Key))
                            {
                                int insterIndex = window.GetInsertMethodIndex(originScript);
                                if (insterIndex < 0)
                                {
                                    Debug.LogWarning($"未找到 UI 事件插入位置，已跳过方法：{item.Key}");
                                    continue;
                                }
                                // 插入新增的方法
                                originScript = window.scriptContent = originScript.Insert(insterIndex, "\n" + item.Value + "\n\t\t");
                                isInsterSuccess = true;
                            }
                        }
                    }


                    if (fieldList != null)
                    {
                        // Item 脚本增量插入事件绑定
                        foreach (var item in fieldList)
                        {
                            string field = $"{item.fieldName}{item.fieldType}";
                            string type = item.fieldType;
                            string methodName = "On" + item.fieldName;
                            string suffix;
                            StringBuilder sb = new StringBuilder();

                            // 根据组件类型，生成不同的事件监听代码
                            if (type.Contains("Button"))
                            {
                                suffix = "ButtonClick";
                                sb.AppendLine($"\t\t\t{field}.onClick.AddListener({methodName}{suffix});");
                            }
                            else if (type.Contains("InputField"))
                            {
                                suffix = "InputChange";
                                sb.AppendLine($"\t\t\t{field}.onValueChanged.AddListener({methodName}{suffix});");
                                suffix = "InputEnd";
                                sb.AppendLine($"\t\t\t{field}.onEndEdit.AddListener({methodName}{suffix});");
                            }
                            else if (type.Contains("Toggle"))
                            {
                                suffix = "ToggleChange";
                                sb.AppendLine($"\t\t\t{field}.onValueChanged.AddListener({methodName}{suffix});");
                            }
                            else
                            {
                                continue;
                            }

                            // 避免重复添加事件监听
                            if (!originScript.Contains($"AddListener({methodName}{suffix})"))
                            {
                                // BindItems：使用占位符插入
                                sb.Insert(0, "//按钮事件自动注册绑定\n");
                                originScript = window.scriptContent = originScript.Replace("//按钮事件自动注册绑定", $"{sb}");
                                isInsterSuccess = true;
                            }
                        }
                    }
                }

                // 如果没有成功插入任何代码，则显示原始脚本
                if (isInsterSuccess == false)
                {
                    window.scriptContent = originScript;
                }
            }

            originScript = null;
            _insertDic = null;
            window.SetPreviewFiles(window.scriptContent, window.filePath, false, extraFiles);
            window.Show();
        }

        /// <summary>
        /// 绘制编辑器窗口UI
        /// </summary>
        public void OnGUI()
        {
            DrawFileTabs();
            SyncSelectedFile();

            float reservedHeight = scriptFiles.Count > 1 ? 150f : 110f;
            float previewHeight = Mathf.Max(220f, position.height - reservedHeight);

            //绘制ScroView
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(previewHeight));
            scriptContent = EditorGUILayout.TextArea(scriptContent);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            //绘制脚本生成路径
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("脚本生成路径", filePath);
            if (GUILayout.Button("选择路径", GUILayout.Width(80)))
            {
                // 打开文件夹选择面板，并保存选择的路径
                string oldFilePath = filePath;
                filePath = EditorUtility.OpenFolderPanel("脚本生成路径", filePath, "WSUI") + "/" + mFileName;
                ApplySelectedFilePath(oldFilePath, filePath);
                EditorPrefs.SetString("GeneratorClassPath", filePath);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            //绘制按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成脚本", GUILayout.Height(30)))
            {
                //按钮事件
                ButtonClick();
            }
            EditorGUILayout.EndHorizontal();

        }

        /// <summary>
        /// "生成脚本"按钮的点击事件处理
        /// </summary>
        public void ButtonClick()
        {
            SyncSelectedFile();

            foreach (ScriptPreviewFile file in scriptFiles)
            {
                string directory = Path.GetDirectoryName(file.FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(file.FilePath, file.Content, Encoding.UTF8);
                Debug.Log("Create Code finish! Cs path:" + file.FilePath);
            }

            onBeforeGenerate?.Invoke();
            scriptContent = string.Empty;
            AssetDatabase.Refresh(); // 刷新AssetDatabase以在Unity编辑器中显示新文件
            if (EditorUtility.DisplayDialog("自动化工具", "生成脚本成功！", "确定"))
            {
                Close(); // 关闭窗口
            }
        }
        /// <summary>
        /// 获取插入方法的代码下标
        /// </summary>
        /// <param name="content">脚本内容</param>
        /// <returns>插入点的索引</returns>
        public int GetInsertMethodIndex(string content)
        {
            //找到UI事件组件下面的第一个public 所在的位置 进行插入
            Regex regex = new Regex("UI组件事件");
            Match match = regex.Match(content);
            if (match.Success)
            {
                int lineEnd = content.IndexOf('\n', match.Index);
                return lineEnd >= 0 ? lineEnd + 1 : match.Index + match.Length;
            }

            // 没有旧标记时，退回到类结束前，保证只追加缺失事件方法。
            int classEndIndex = content.LastIndexOf("\n\t}", System.StringComparison.Ordinal);
            return classEndIndex >= 0 ? classEndIndex : -1;
        }

        /// <summary>
        /// 获取插入字段的代码下标
        /// </summary>
        /// <param name="content">脚本内容</param>
        /// <returns>插入点的索引</returns>
        public int GetInsertFieldIndex(string content)
        {
            //找到UI事件组件下面的第一个public 所在的位置 进行插入
            Regex regex = new Regex("自定义字段");
            Match match = regex.Match(content);
            if (match.Success)
            {
                return match.Index + 6;
            }

            return -1;

            /*Regex regex1 = new Regex("public");
            MatchCollection matchColltion = regex1.Matches(content);

            // 找到 "自定义字段" 注释后的第一个 public 关键字位置
            for (int i = 0; i < matchColltion.Count; i++)
            {
                if (matchColltion[i].Index > match.Index)
                {
                    return matchColltion[i].Index;
                }
            }
            return -1; // 未找到插入点*/
        }

        /// <summary>
        /// 获取 InitComponent 方法内的插入点（插在第一个匹配的方法结束大括号前）
        /// </summary>
        public int GetInitComponentInsertIndex(string content)
        {
            var match = Regex.Match(content, @"void\s+InitComponent\s*\(");
            if (!match.Success)
                return -1;

            // 从方法声明开始查找方法体
            int braceStart = content.IndexOf('{', match.Index);
            if (braceStart < 0)
                return -1;

            int depth = 0;
            for (int i = braceStart; i < content.Length; i++)
            {
                if (content[i] == '{')
                    depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // 插在方法结束的 '}' 之前
                        return i;
                    }
                }
            }

            return -1;
        }

        private void SetPreviewFiles(
            string mainContent,
            string mainFilePath,
            bool overwriteMain,
            List<GeneratedScriptFile> extraFiles)
        {
            scriptFiles.Add(new ScriptPreviewFile
            {
                Content = mainContent,
                FilePath = mainFilePath,
                FileName = Path.GetFileName(mainFilePath),
                OverwriteExisting = overwriteMain
            });

            if (extraFiles != null)
            {
                foreach (GeneratedScriptFile file in extraFiles)
                {
                    scriptFiles.Add(new ScriptPreviewFile
                    {
                        Content = file.Content,
                        FilePath = file.FilePath,
                        FileName = Path.GetFileName(file.FilePath),
                        OverwriteExisting = file.OverwriteExisting
                    });
                }
            }

            selectedFileIndex = 0;
            LoadSelectedFile();
        }

        private void DrawFileTabs()
        {
            if (scriptFiles.Count <= 1)
            {
                return;
            }

            EditorGUILayout.LabelField("生成文件", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < scriptFiles.Count; i++)
            {
                bool selected = i == selectedFileIndex;
                string label = selected ? $"● {scriptFiles[i].FileName}" : scriptFiles[i].FileName;
                if (GUILayout.Toggle(selected, label, "Button"))
                {
                    if (selectedFileIndex != i)
                    {
                        SyncSelectedFile();
                        selectedFileIndex = i;
                        LoadSelectedFile();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void LoadSelectedFile()
        {
            if (selectedFileIndex < 0 || selectedFileIndex >= scriptFiles.Count)
            {
                return;
            }

            ScriptPreviewFile selectedFile = scriptFiles[selectedFileIndex];
            scriptContent = selectedFile.Content;
            filePath = selectedFile.FilePath;
            mFileName = selectedFile.FileName;
        }

        private void SyncSelectedFile()
        {
            if (selectedFileIndex < 0 || selectedFileIndex >= scriptFiles.Count)
            {
                return;
            }

            ScriptPreviewFile selectedFile = scriptFiles[selectedFileIndex];
            selectedFile.Content = scriptContent;
            selectedFile.FilePath = filePath;
            selectedFile.FileName = Path.GetFileName(filePath);
        }

        private void ApplySelectedFilePath(string oldFilePath, string newFilePath)
        {
            SyncSelectedFile();

            if (selectedFileIndex != 0 || scriptFiles.Count <= 1)
            {
                return;
            }

            string oldDirectory = Path.GetDirectoryName(oldFilePath);
            string newDirectory = Path.GetDirectoryName(newFilePath);
            string newMainName = Path.GetFileNameWithoutExtension(newFilePath);

            if (string.IsNullOrEmpty(oldDirectory) || string.IsNullOrEmpty(newDirectory))
            {
                return;
            }

            for (int i = 1; i < scriptFiles.Count; i++)
            {
                ScriptPreviewFile file = scriptFiles[i];
                string extension = file.FileName.EndsWith(".generated.cs")
                    ? ".generated.cs"
                    : Path.GetExtension(file.FileName);
                file.FilePath = Path.Combine(newDirectory, newMainName + extension).Replace("\\", "/");
                file.FileName = Path.GetFileName(file.FilePath);
            }
        }

    }
}
