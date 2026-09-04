using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace WS_Modules
{
    /// <summary>
    /// 以注释和字符串感知的有限状态机解析事件调用，不依赖 Roslyn。
    /// </summary>
    internal sealed class EventSourceParser
    {
        private static readonly HashSet<string> DefaultEventSystemReceivers =
            new HashSet<string>(StringComparer.Ordinal)
                { "EventSystem", "GlobalEventSystem", "WSEventSystem" };

        /// <summary>事件中心种类。</summary>
        internal enum EventCenterKind
        {
            /// <summary>字符串键事件中心。</summary>
            String,
            /// <summary>整数键事件中心。</summary>
            Int,
            /// <summary>Type 键事件中心。</summary>
            Type,
        }

        /// <summary>解析一个脚本中的事件调用。</summary>
        /// <param name="lines">源码行。</param><param name="script">脚本资源。</param>
        /// <returns>解析出的调用集合。</returns>
        public IReadOnlyList<ParsedEventCall> Parse(string[] lines, MonoScript script)
        {
            List<Token> tokens = Tokenize(lines);
            bool businessContext = HasBusinessContext(lines);
            int[] scopeStarts = BuildScopeStarts(tokens);
            Dictionary<string, List<VariableType>> variableTypes = BuildTypeMap(tokens, scopeStarts);
            HashSet<string> eventSystemReceivers = BuildEventSystemReceivers(lines);
            var result = new List<ParsedEventCall>();
            for (int i = 0; i < tokens.Count; i++)
            {
                string method = tokens[i].Value;
                bool businessRegister = method == "RegisterEvent";
                bool businessTrigger = method == "SendEvent";
                bool directEvent = IsDirectEventMethod(method);
                if (!businessRegister && !businessTrigger && !directEvent) continue;
                if ((businessRegister || businessTrigger) && !businessContext) continue;
                if (directEvent && !HasEventSystemReceiver(tokens, i, eventSystemReceivers)) continue;
                if (!TryFindCall(tokens, i, out int openParen, out int closeParen)) continue;

                bool register = businessRegister || method.StartsWith("Register_", StringComparison.Ordinal);
                EventCallSource source = businessRegister || businessTrigger
                    ? EventCallSource.BusinessArchitecture
                    : EventCallSource.EventSystem;
                EventCenterKind center = GetCenterKind(method, source);
                string expression = ExtractExpression(tokens, i, openParen, closeParen, center, source, variableTypes,
                    scopeStarts);
                bool genericForwarding = IsGenericForwarding(expression, center, source, lines, tokens[i].Line);
                result.Add(new ParsedEventCall(script, tokens[i].Line, register, source, center,
                    expression, expression.Length > 0 ? expression : GetArgumentText(tokens, openParen, closeParen),
                    genericForwarding));
                i = closeParen;
            }

            return result;
        }

        /// <summary>识别只包含类型参数的 typeof 表达式，标记为泛型转发位置。</summary>
        /// <param name="expression">事件类型表达式。</param><param name="center">事件中心类别。</param><param name="source">调用来源。</param><returns>是否为泛型转发。</returns>
        private static bool IsGenericForwarding(string expression, EventCenterKind center, EventCallSource source,
            IReadOnlyList<string> lines, int callLine)
        {
            if (source != EventCallSource.EventSystem || center != EventCenterKind.Type ||
                string.IsNullOrEmpty(expression) || expression[0] != 'T') return false;
            for (int i = 1; i < expression.Length; i++)
                if (!char.IsLetterOrDigit(expression[i]) && expression[i] != '_')
                    return false;
            // 只有调用附近确实存在同名泛型声明时才标记，避免 TaskEvent 等普通类型被误判。
            int firstLine = Math.Max(0, callLine - 1 - 8);
            int lastLine = Math.Min(lines.Count - 1, callLine - 1);
            string declarationMarker = "<" + expression + ">";
            for (int i = firstLine; i <= lastLine; i++)
                if (lines[i].Replace(" ", string.Empty).Contains(declarationMarker, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>从 using 别名中收集指向 EventSystem 的调用接收者。</summary>
        /// <param name="lines">源码行。</param><returns>可识别的接收者名称。</returns>
        private static HashSet<string> BuildEventSystemReceivers(IEnumerable<string> lines)
        {
            var receivers = new HashSet<string>(DefaultEventSystemReceivers, StringComparer.Ordinal);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) || trimmed.IndexOf('=') < 0 ||
                    trimmed.IndexOf("EventSystem", StringComparison.Ordinal) < 0) continue;
                int nameStart = "using ".Length;
                int equals = trimmed.IndexOf('=');
                string alias = trimmed.Substring(nameStart, equals - nameStart).Trim();
                if (alias.Length > 0) receivers.Add(alias);
            }

            return receivers;
        }

        /// <summary>收集简单局部变量声明，供省略泛型的 Business 事件调用解析。</summary>
        /// <param name="tokens">源码标记。</param><returns>变量到类型的映射。</returns>
        private static int[] BuildScopeStarts(IReadOnlyList<Token> tokens)
        {
            var starts = new int[tokens.Count];
            int current = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                starts[i] = current;
                if (tokens[i].Value == "{") current = i;
                else if (tokens[i].Value == "}")
                {
                    current = -1;
                    for (int j = i - 1; j >= 0; j--)
                        if (tokens[j].Value == "{")
                        {
                            current = starts[j];
                            break;
                        }
                }
            }

            return starts;
        }

        /// <summary>建立变量名到声明类型和作用域的索引。</summary>
        /// <param name="tokens">源码标记。</param><param name="scopeStarts">每个标记所在作用域起点。</param><returns>变量声明索引。</returns>
        private static Dictionary<string, List<VariableType>> BuildTypeMap(IReadOnlyList<Token> tokens,
            IReadOnlyList<int> scopeStarts)
        {
            var map = new Dictionary<string, List<VariableType>>(StringComparer.Ordinal);
            for (int i = 1; i + 1 < tokens.Count; i++)
            {
                if (tokens[i + 1].Value != "=" && tokens[i + 1].Value != ";" && tokens[i + 1].Value != ",") continue;
                string type = ResolveDeclaredType(tokens, i - 1);
                if (string.IsNullOrEmpty(type)) continue;
                if (!map.TryGetValue(tokens[i].Value, out List<VariableType> declarations))
                {
                    declarations = new List<VariableType>();
                    map.Add(tokens[i].Value, declarations);
                }

                declarations.Add(new VariableType(type, tokens[i].Line, scopeStarts[i]));
            }

            return map;
        }

        /// <summary>解析变量名前的普通类型或泛型委托中的事件类型。</summary>
        /// <param name="tokens">源码标记。</param><param name="typeEnd">类型结束位置。</param><returns>事件类型文本。</returns>
        private static string ResolveDeclaredType(IReadOnlyList<Token> tokens, int typeEnd)
        {
            if (typeEnd < 0 || tokens[typeEnd].Value == "var") return string.Empty;
            if (tokens[typeEnd].Value != ">") return tokens[typeEnd].Value;

            int depth = 0;
            for (int i = typeEnd; i >= 0; i--)
            {
                if (tokens[i].Value == ">") depth++;
                else if (tokens[i].Value == "<" && --depth == 0)
                    return ReadGenericType(tokens, i);
            }

            return string.Empty;
        }

        /// <summary>按调用所在行选择最近可见的变量声明。</summary>
        /// <param name="variableTypes">变量声明索引。</param><param name="name">变量名。</param><param name="line">调用行。</param><returns>类型文本。</returns>
        private static string ResolveVariableType(IReadOnlyDictionary<string, List<VariableType>> variableTypes,
            string name,
            int line, int scopeStart, IReadOnlyList<int> scopeStarts)
        {
            if (!variableTypes.TryGetValue(name, out List<VariableType> declarations)) return string.Empty;
            string resolved = string.Empty;
            int nearestLine = -1;
            foreach (VariableType declaration in declarations)
            {
                if (IsScopeVisible(declaration.ScopeStart, scopeStart, scopeStarts) &&
                    declaration.Line <= line && declaration.Line >= nearestLine)
                {
                    nearestLine = declaration.Line;
                    resolved = declaration.Type;
                }
            }

            return resolved;
        }

        /// <summary>判断声明作用域是否为当前代码块或其外层作用域。</summary>
        /// <param name="declarationScope">声明作用域。</param><param name="currentScope">调用作用域。</param><param name="scopeStarts">作用域父链。</param><returns>是否可见。</returns>
        private static bool IsScopeVisible(int declarationScope, int currentScope, IReadOnlyList<int> scopeStarts)
        {
            for (int scope = currentScope; scope >= 0; scope = scopeStarts[scope])
                if (scope == declarationScope)
                    return true;
            return declarationScope < 0;
        }

        /// <summary>通过基类或能力接口判断脚本是否属于 BusinessArchitecture 调用上下文。</summary>
        /// <param name="lines">源码行。</param><returns>是否为业务架构组件。</returns>
        private static bool HasBusinessContext(string[] lines)
        {
            foreach (string line in lines)
            {
                if (line.IndexOf("AbstractSystem", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("AbstractManager", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("AbstractCommand", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("AbstractQuery", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("ICanSendEvent", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("ICanRegisterEvent", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("Architecture<", StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>判断是否为直接事件中心方法。</summary>
        /// <param name="method">方法名。</param><returns>是否匹配。</returns>
        private static bool IsDirectEventMethod(string method)
        {
            return method == "Register_String" || method == "Register_Int" || method == "Register_Type" ||
                   method == "EventTrigger_String" || method == "EventTrigger_Int" || method == "EventTrigger_Type";
        }

        /// <summary>检查调用链中是否出现已知 EventSystem 接收者。</summary>
        /// <param name="tokens">源码标记。</param><param name="methodIndex">方法下标。</param><param name="receivers">已知接收者。</param><returns>是否匹配。</returns>
        private static bool HasEventSystemReceiver(IReadOnlyList<Token> tokens, int methodIndex, ISet<string> receivers)
        {
            for (int i = methodIndex - 1; i >= 0 && methodIndex - i <= 5; i--)
                if (receivers.Contains(tokens[i].Value))
                    return true;
            return false;
        }

        /// <summary>定位调用括号并按层级找到对应右括号。</summary>
        /// <param name="tokens">源码标记。</param><param name="methodIndex">方法下标。</param>
        /// <param name="openParen">左括号下标。</param><param name="closeParen">右括号下标。</param>
        /// <returns>是否找到完整调用。</returns>
        private static bool TryFindCall(IReadOnlyList<Token> tokens, int methodIndex, out int openParen,
            out int closeParen)
        {
            openParen = -1;
            closeParen = -1;
            for (int i = methodIndex + 1; i < tokens.Count && i <= methodIndex + 5; i++)
            {
                if (tokens[i].Value == "(")
                {
                    openParen = i;
                    break;
                }

                // 泛型参数本身由标识符、命名空间点和逗号组成，不能在这里提前判定为无效。
                if (tokens[i].Value == ";" || tokens[i].Value == "{" || tokens[i].Value == "}") return false;
            }

            if (openParen < 0) return false;
            int depth = 0;
            for (int i = openParen; i < tokens.Count; i++)
            {
                if (tokens[i].Value == "(") depth++;
                else if (tokens[i].Value == ")" && --depth == 0)
                {
                    closeParen = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>提取 Type、String、Int 或业务事件的身份表达式。</summary>
        /// <param name="tokens">源码标记。</param><param name="methodIndex">方法下标。</param>
        /// <param name="openParen">左括号下标。</param><param name="closeParen">右括号下标。</param>
        /// <param name="center">事件中心种类。</param><param name="source">调用来源。</param><param name="variableTypes">变量类型索引。</param>
        /// <returns>身份表达式。</returns>
        private static string ExtractExpression(IReadOnlyList<Token> tokens, int methodIndex, int openParen,
            int closeParen, EventCenterKind center, EventCallSource source,
            IReadOnlyDictionary<string, List<VariableType>> variableTypes, IReadOnlyList<int> scopeStarts)
        {
            if (source == EventCallSource.BusinessArchitecture)
            {
                if (methodIndex + 1 < tokens.Count && tokens[methodIndex + 1].Value == "<")
                    return ReadGenericType(tokens, methodIndex + 1);
                int first = openParen + 1;
                if (first + 1 < closeParen && tokens[first].Value == "new") return tokens[first + 1].Value;
                if (first < closeParen)
                {
                    string variableType = ResolveVariableType(variableTypes, tokens[first].Value,
                        tokens[methodIndex].Line,
                        scopeStarts[methodIndex], scopeStarts);
                    if (!string.IsNullOrEmpty(variableType)) return variableType;
                }

                return string.Empty;
            }

            int argument = openParen + 1;
            if (center == EventCenterKind.Type && argument + 2 < closeParen && tokens[argument].Value == "typeof")
                return ReadTypeofType(tokens, argument + 2, closeParen);
            return ReadTopLevelArgument(tokens, argument, closeParen);
        }

        /// <summary>读取 typeof 中可能包含命名空间和嵌套泛型的类型文本。</summary>
        /// <param name="tokens">源码标记。</param><param name="start">类型起点。</param><param name="end">调用参数终点。</param>
        /// <returns>类型文本。</returns>
        private static string ReadTypeofType(IReadOnlyList<Token> tokens, int start, int end)
        {
            var builder = new StringBuilder();
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                if (tokens[i].Value == "(") depth++;
                else if (tokens[i].Value == ")" && depth-- == 0) break;
                if (tokens[i].Value == "," && depth == 0) break;
                builder.Append(tokens[i].Value);
            }

            return builder.ToString().Trim();
        }

        /// <summary>读取显式泛型的第一个类型参数。</summary>
        /// <param name="tokens">源码标记。</param><param name="start">小于号下标。</param><returns>类型名。</returns>
        private static string ReadGenericType(IReadOnlyList<Token> tokens, int start)
        {
            var builder = new StringBuilder();
            int depth = 0;
            for (int i = start + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Value == "<") depth++;
                else if (tokens[i].Value == ">" && depth-- == 0) break;
                else if (tokens[i].Value == "," && depth == 0) break;
                builder.Append(tokens[i].Value);
            }

            return builder.ToString().Trim();
        }

        /// <summary>读取括号层级为零时的第一个参数。</summary>
        /// <param name="tokens">源码标记。</param><param name="start">参数起点。</param><param name="end">参数终点。</param>
        /// <returns>参数表达式。</returns>
        private static string ReadTopLevelArgument(IReadOnlyList<Token> tokens, int start, int end)
        {
            var builder = new StringBuilder();
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                string value = tokens[i].Value;
                if (value == "(" || value == "[" || value == "{") depth++;
                else if (value == ")" || value == "]" || value == "}") depth--;
                if (value == "," && depth == 0) break;
                builder.Append(value);
            }

            return builder.ToString().Trim();
        }

        /// <summary>获取无法解析时的原始第一个参数。</summary>
        /// <param name="tokens">源码标记。</param><param name="openParen">左括号下标。</param><param name="closeParen">右括号下标。</param>
        /// <returns>参数文本。</returns>
        private static string GetArgumentText(IReadOnlyList<Token> tokens, int openParen, int closeParen)
            => ReadTopLevelArgument(tokens, openParen + 1, closeParen);

        /// <summary>根据方法后缀确定事件中心种类。</summary>
        /// <param name="method">方法名。</param><param name="source">调用来源。</param><returns>中心种类。</returns>
        private static EventCenterKind GetCenterKind(string method, EventCallSource source)
        {
            if (source == EventCallSource.BusinessArchitecture || method.EndsWith("_Type", StringComparison.Ordinal))
                return EventCenterKind.Type;
            if (method.EndsWith("_String", StringComparison.Ordinal)) return EventCenterKind.String;
            return EventCenterKind.Int;
        }

        /// <summary>把源码转换成跳过注释和字符串内容的标记。</summary>
        /// <param name="lines">源码行。</param><returns>带行号的标记集合。</returns>
        private static List<Token> Tokenize(string[] lines)
        {
            var result = new List<Token>();
            // 跳过注释和字符串，按标点符号拆分标记。标点符号包括括号、逗号、分号、点和运算符。
            bool blockComment = false;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                for (int i = 0; i < line.Length; i++)
                {
                    if (blockComment)
                    {
                        int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                        if (end < 0) break;
                        blockComment = false;
                        i = end + 1;
                        continue;
                    }

                    // 跳过单行注释和块注释，保留字符串内容。
                    if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                    {
                        blockComment = true;
                        i++;
                        continue;
                    }

                    if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/') break;
                    // 跳过空白符，保留标点符号和标识符。
                    if (char.IsWhiteSpace(line[i])) continue;

                    // 跳过字符串，保留字符串内容。
                    if (line[i] == '"' || (line[i] == '@' && i + 1 < line.Length && line[i + 1] == '"'))
                    {
                        int quote = line[i] == '@' ? i + 1 : i;
                        int end = SkipString(line, quote);
                        result.Add(new Token(line.Substring(i, end - i + 1), lineIndex + 1));
                        i = end;
                        continue;
                    }

                    if (char.IsLetter(line[i]) || line[i] == '_')
                    {
                        int start = i++;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                        result.Add(new Token(line.Substring(start, i - start), lineIndex + 1));
                        i--;
                        continue;
                    }

                    result.Add(new Token(line[i].ToString(), lineIndex + 1));
                }
            }

            return result;
        }

        /// <summary>跳过一个普通字符串及其转义字符。</summary>
        /// <param name="line">源码行。</param><param name="start">起始引号。</param><returns>结束下标。</returns>
        private static int SkipString(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (line[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (line[i] == '"') return i;
            }

            return line.Length - 1;
        }

        /// <summary>变量声明索引项。</summary>
        private readonly struct VariableType
        {
            /// <summary>创建变量声明索引项。</summary>
            /// <param name="type">变量类型。</param><param name="line">声明行。</param>
            public VariableType(string type, int line, int scopeStart)
            {
                Type = type;
                Line = line;
                ScopeStart = scopeStart;
            }

            /// <summary>类型文本。</summary><value>变量类型。</value>
            public string Type { get; }
            /// <summary>声明行。</summary><value>源码行号。</value>
            public int Line { get; }
            /// <summary>声明所在的最近代码块起点。</summary><value>用于区分局部作用域。</value>
            public int ScopeStart { get; }
        }

        /// <summary>源码标记。</summary>
        private readonly struct Token
        {
            /// <summary>创建标记。</summary><param name="value">文本。</param><param name="line">行号。</param>
            public Token(string value, int line)
            {
                Value = value;
                Line = line;
            }

            /// <summary>文本。</summary><value>标记文本。</value>
            public string Value { get; }
            /// <summary>行号。</summary><value>所在行。</value>
            public int Line { get; }
        }
    }

    /// <summary>描述解析出的一个事件调用。</summary>
    internal sealed class ParsedEventCall
    {
        /// <summary>创建解析结果。</summary>
        /// <param name="script">脚本。</param><param name="line">行号。</param><param name="isRegister">是否注册。</param>
        /// <param name="source">来源。</param><param name="center">中心种类。</param><param name="expression">身份。</param><param name="displayText">显示文本。</param><param name="isGenericForwarding">是否为泛型转发。</param>
        public ParsedEventCall(MonoScript script, int line, bool isRegister, EventCallSource source,
            EventSourceParser.EventCenterKind center, string expression, string displayText, bool isGenericForwarding)
        {
            Script = script;
            Line = line;
            IsRegister = isRegister;
            Source = source;
            Center = center;
            Expression = expression;
            DisplayText = displayText;
            IsGenericForwarding = isGenericForwarding;
        }

        /// <summary>脚本。</summary><value>脚本资源。</value>
        public MonoScript Script { get; }
        /// <summary>行号。</summary><value>调用起始行。</value>
        public int Line { get; }
        /// <summary>是否为监听注册。</summary><value>角色。</value>
        public bool IsRegister { get; }
        /// <summary>调用来源。</summary><value>来源。</value>
        public EventCallSource Source { get; }
        /// <summary>事件中心种类。</summary><value>中心。</value>
        public EventSourceParser.EventCenterKind Center { get; }
        /// <summary>可归并的事件身份。</summary><value>身份表达式。</value>
        public string Expression { get; }
        /// <summary>面板显示文本。</summary><value>原始或解析后的文本。</value>
        public string DisplayText { get; }
        /// <summary>是否为泛型转发调用。</summary><value>具体事件类型由调用方决定。</value>
        public bool IsGenericForwarding { get; }
    }
}