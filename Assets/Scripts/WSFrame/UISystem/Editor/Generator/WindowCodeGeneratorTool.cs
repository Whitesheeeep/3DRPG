using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.UIModule
{
    public static class WindowCodeGeneratorTool
    {
        static Dictionary<string, string> methodDic = new Dictionary<string, string>();

        private static WSFrameSetting GetSetting()
        {
            var settings = AssetDatabase.FindAssets("t:WSFrameSetting");
            if (settings.Length == 0)
            {
                Debug.LogError("Can not find WSFrameSetting asset.");
                return null;
            }
            var path = AssetDatabase.GUIDToAssetPath(settings[0]);
            return AssetDatabase.LoadAssetAtPath<WSFrameSetting>(path);
        }

        [MenuItem("GameObject/UI自动绑定工具/生成Window脚本(Shift+V) #V", false, 0)]
        internal static void CreateFindComponentScripts()
        {
            GameObject obj = Selection.objects.First() as GameObject; //获取到当前选择的物体
            if (obj == null)
            {
                Debug.LogError("需要选择 GameObject");
                return;
            }

            var setting = WSFrameRoot.Instance?.FrameSetting ?? GetSetting();
            if (setting == null) return;

            //设置脚本生成路径
            if (!Directory.Exists(setting.uiManagerSetting.WindowGeneratorPath))
            {
                Directory.CreateDirectory(setting.uiManagerSetting.WindowGeneratorPath);
            }

            //生成CS脚本
            string csContnet = CreateWindowCs(obj.name);
            string generatedContent = CreateWindowGeneratedCs(obj.name);

            Debug.Log("CsConent:\n" + csContnet);
            string cspath = setting.uiManagerSetting.WindowGeneratorPath + "/" + obj.name +
                            ".cs";
            string generatedPath = setting.uiManagerSetting.WindowGeneratorPath + "/" + obj.name +
                                   ".generated.cs";
            ScriptDisplayWindow.ShowWindow(
                csContnet,
                cspath,
                methodDic,
                extraFiles: new List<ScriptDisplayWindow.GeneratedScriptFile>
                {
                    new(generatedContent, generatedPath, true)
                });
        }

        /// <summary>
        /// 生成Window脚本
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string CreateWindowCs(string name)
        {
            //储存字段名称
            string datalistJson = PlayerPrefs.GetString(GeneratorConfig.OBJDATALIST_KEY);
            List<EditorObjectData> objDatalist = JsonConvert.DeserializeObject<List<EditorObjectData>>(datalistJson);
            methodDic.Clear();
            StringBuilder sb = new StringBuilder();
            string nameSpaceName = "WS_Modules.UIModule";
            //添加引用
            sb.AppendLine("// WSFrame WindowCode 生成规则：");
            sb.AppendLine("// 1. 本文件首次由生成器创建，创建后作为手写窗口逻辑入口。");
            sb.AppendLine("// 2. 后续重新生成不会整体覆盖本文件。");
            sb.AppendLine("// 3. 生命周期方法、API 方法、MVVM 绑定和业务逻辑不会被生成器修改。");
            sb.AppendLine("// 4. UI 事件方法一旦存在，生成器不会覆盖其方法体。");
            sb.AppendLine("// 5. 当 UI 新增可绑定事件组件时，生成器只会追加缺失的事件空方法。");
            sb.AppendLine("// 6. 当 UI 删除、重命名或修改组件类型时，旧事件方法不会自动删除，请手动清理。");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            //生成命名空间
            if (!string.IsNullOrEmpty(nameSpaceName))
            {
                sb.AppendLine($"namespace {nameSpaceName}");
                sb.AppendLine("{");
            }

            //生成类命
            sb.AppendLine($"\tpublic partial class {name}:WindowBase");
            sb.AppendLine("\t{");
            sb.AppendLine("\t");

            //生成生命周期函数 Awake
            sb.AppendLine("\t");
            sb.AppendLine($"\t\t #region 生命周期函数");
            sb.AppendLine($"\t\t //调用机制与Mono Awake一致");
            sb.AppendLine("\t\t public override void OnAwake()");
            sb.AppendLine("\t\t {");
            sb.AppendLine("\t\t\t BindGeneratedComponents();");
            sb.AppendLine("\t\t\t base.OnAwake();");
            sb.AppendLine("\t\t }");
            //OnShow
            sb.AppendLine($"\t\t //物体显示时执行");
            sb.AppendLine("\t\t public override void OnShow()");
            sb.AppendLine("\t\t {");
            sb.AppendLine("\t\t\t base.OnShow();");
            sb.AppendLine("\t\t }");
            //OnHide
            sb.AppendLine($"\t\t //物体隐藏时执行");
            sb.AppendLine("\t\t public override void OnHide()");
            sb.AppendLine("\t\t {");
            sb.AppendLine("\t\t\t base.OnHide();");
            sb.AppendLine("\t\t }");

            //OnDestroy
            sb.AppendLine($"\t\t //物体销毁时执行");
            sb.AppendLine("\t\t public override void OnDestroy()");
            sb.AppendLine("\t\t {");
            sb.AppendLine("\t\t\t base.OnDestroy();");
            sb.AppendLine("\t\t }");

            sb.AppendLine($"\t\t #endregion");

            //API Function 
            sb.AppendLine($"\t\t #region API Function");
            sb.AppendLine($"\t\t    ");
            sb.AppendLine($"\t\t #endregion");

            //UI组件事件生成
            sb.AppendLine($"\t\t #region UI组件事件");
            foreach (var item in objDatalist)
            {
                string type = item.fieldType;
                string methodName = "On" + item.fieldName;
                string suffix = "";
                if (type.Contains("Button"))
                {
                    suffix = "ButtonClick";
                    CreateMethod(sb, ref methodDic, methodName + suffix);
                }
                else if (type.Contains("InputField"))
                {
                    suffix = "InputChange";
                    CreateMethod(sb, ref methodDic, methodName + suffix, "string text");
                    suffix = "InputEnd";
                    CreateMethod(sb, ref methodDic, methodName + suffix, "string text");
                }
                else if (type.Contains("Toggle"))
                {
                    suffix = "ToggleChange";
                    CreateMethod(sb, ref methodDic, methodName + suffix, "bool state,Toggle toggle");
                }
            }

            sb.AppendLine($"\t\t #endregion");

            sb.AppendLine("\t}");
            if (!string.IsNullOrEmpty(nameSpaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成可覆盖的 WindowCode 自动绑定 partial 文件。
        /// </summary>
        /// <param name="name">窗口名称。</param>
        /// <returns>自动绑定 partial 文件内容。</returns>
        public static string CreateWindowGeneratedCs(string name)
        {
            StringBuilder sb = new StringBuilder();
            string nameSpaceName = "WS_Modules.UIModule";

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 此文件由 WSFrame 自动生成。");
            sb.AppendLine("// 该文件用于维护 WindowCode 的自动绑定字段和绑定入口。");
            sb.AppendLine("// 该文件可能会在下次生成时被整体覆盖，请不要在此文件中手写业务逻辑。");
            sb.AppendLine("// UI 事件方法和窗口业务逻辑请写在同名 WindowCode 主文件中。");
            sb.AppendLine("// </auto-generated>");

            if (!string.IsNullOrEmpty(nameSpaceName))
            {
                sb.AppendLine($"namespace {nameSpaceName}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"\tpublic partial class {name}");
            sb.AppendLine("\t{");
            sb.AppendLine($"\t\tpublic {name}DataComponent dataCompt;");
            sb.AppendLine();
            sb.AppendLine("\t\tprivate void BindGeneratedComponents()");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\tdataCompt = GameObject.GetComponent<{name}DataComponent>();");
            sb.AppendLine("\t\t\tFullScreenWindow = dataCompt.IsFullWindow;");
            sb.AppendLine("\t\t\tSetDoAnimation(dataCompt.DoAnimation);");
            sb.AppendLine("\t\t\tdataCompt.InitComponent(this);");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");

            if (!string.IsNullOrEmpty(nameSpaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成UI事件方法
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="methodDic"></param>
        /// <param name="modthName"></param>
        /// <param name="param"></param>
        public static void CreateMethod(StringBuilder sb, ref Dictionary<string, string> methodDic, string methodName,
            string param = "")
        {
            //声明UI组件事件
            sb.AppendLine("\t\t /// <summary>");
            sb.AppendLine("\t\t /// UI 事件方法。生成器只在方法缺失时追加，后续不会覆盖方法体。");
            sb.AppendLine("\t\t /// </summary>");
            sb.AppendLine($"\t\t public void {methodName}({param})");
            sb.AppendLine("\t\t {");
            sb.AppendLine("\t\t");
            if (methodName == "OnCloseButtonClick")
            {
                sb.AppendLine("\t\t\tHideWindow();");
            }

            sb.AppendLine("\t\t }");

            //存储UI组件事件 提供给后续新增代码使用
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("\t\t /// <summary>");
            builder.AppendLine("\t\t /// UI 事件方法。生成器只在方法缺失时追加，后续不会覆盖方法体。");
            builder.AppendLine("\t\t /// </summary>");
            builder.AppendLine($"\t\t public void {methodName}({param})");
            builder.AppendLine("\t\t {");
            builder.AppendLine("\t\t");
            builder.AppendLine("\t\t }");
            methodDic.Add(methodName, builder.ToString());
        }
    }
}
