using System;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 解析并执行可复用的文本筛选表达式，支持括号、AND（&amp;）和 OR（|）。
    /// </summary>
    public static class TextFilterExpression
    {
        #region 解析入口

        /// <summary>解析筛选表达式并返回不可变匹配器或错误信息。</summary>
        /// <param name="expression">待解析的表达式。</param>
        /// <returns>表达式解析结果。</returns>
        public static ParseResult Parse(string expression)
        {
            var parser = new ExpressionParser(expression ?? string.Empty);
            return parser.Parse();
        }

        #endregion

        #region 公开结果类型

        /// <summary>
        /// 表示表达式解析结果；有效结果包含可跨线程复用的只读匹配器。
        /// </summary>
        public sealed class ParseResult
        {
            #region 数据

            /// <summary>创建解析结果。</summary>
            /// <param name="matcher">成功解析出的匹配器。</param>
            /// <param name="errorMessage">失败时的错误信息。</param>
            /// <param name="errorPosition">失败字符的位置，未知时为 -1。</param>
            internal ParseResult(TextMatcher matcher, string errorMessage, int errorPosition)
            {
                Matcher = matcher;
                ErrorMessage = errorMessage ?? string.Empty;
                ErrorPosition = errorPosition;
            }

            /// <summary>表达式是否解析成功。</summary>
            public bool IsValid => Matcher != null;

            /// <summary>成功时可复用的匹配器，失败时为 null。</summary>
            public TextMatcher Matcher { get; }

            /// <summary>失败原因，成功时为空字符串。</summary>
            public string ErrorMessage { get; }

            /// <summary>失败字符在原始表达式中的索引，成功时为 -1。</summary>
            public int ErrorPosition { get; }

            #endregion
        }

        /// <summary>
        /// 根据已经解析的表达式判断文本是否满足筛选条件；实例创建后不再改变。
        /// </summary>
        public sealed class TextMatcher
        {
            #region 依赖字段

            private readonly ExpressionNode _root;

            #endregion

            #region 生命周期与公开操作

            /// <summary>创建不可变匹配器。</summary>
            /// <param name="root">表达式语法树根节点。</param>
            internal TextMatcher(ExpressionNode root)
            {
                _root = root ?? throw new ArgumentNullException(nameof(root));
            }

            /// <summary>按区分大小写的正文包含关系执行匹配。</summary>
            /// <param name="text">待匹配文本。</param>
            /// <returns>文本是否满足表达式。</returns>
            public bool IsMatch(string text)
            {
                return _root.IsMatch(text ?? string.Empty);
            }

            #endregion
        }

        #endregion

        #region 语法树节点

        /// <summary>表达式语法树节点的内部匹配契约。</summary>
        internal abstract class ExpressionNode
        {
            /// <summary>判断文本是否匹配当前节点。</summary>
            /// <param name="text">待匹配文本。</param>
            /// <returns>是否匹配。</returns>
            public abstract bool IsMatch(string text);
        }

        /// <summary>匹配一个普通文本片段。</summary>
        private sealed class ContainsNode : ExpressionNode
        {
            private readonly string _value;

            /// <summary>创建文本片段节点。</summary>
            /// <param name="value">需要在正文中查找的片段。</param>
            public ContainsNode(string value)
            {
                _value = value;
            }

            /// <summary>判断正文是否包含当前片段。</summary>
            /// <param name="text">待匹配正文。</param>
            /// <returns>是否包含片段。</returns>
            public override bool IsMatch(string text)
            {
                return text.IndexOf(_value, StringComparison.Ordinal) >= 0;
            }
        }

        /// <summary>组合两个必须同时满足的节点。</summary>
        private sealed class AndNode : ExpressionNode
        {
            private readonly ExpressionNode _left;
            private readonly ExpressionNode _right;

            /// <summary>创建 AND 节点。</summary>
            /// <param name="left">左侧节点。</param>
            /// <param name="right">右侧节点。</param>
            public AndNode(ExpressionNode left, ExpressionNode right)
            {
                _left = left;
                _right = right;
            }

            /// <summary>判断两侧节点是否都匹配。</summary>
            /// <param name="text">待匹配正文。</param>
            /// <returns>两侧是否都匹配。</returns>
            public override bool IsMatch(string text)
            {
                return _left.IsMatch(text) && _right.IsMatch(text);
            }
        }

        /// <summary>组合两个至少满足其一的节点。</summary>
        private sealed class OrNode : ExpressionNode
        {
            private readonly ExpressionNode _left;
            private readonly ExpressionNode _right;

            /// <summary>创建 OR 节点。</summary>
            /// <param name="left">左侧节点。</param>
            /// <param name="right">右侧节点。</param>
            public OrNode(ExpressionNode left, ExpressionNode right)
            {
                _left = left;
                _right = right;
            }

            /// <summary>判断两侧节点是否至少有一侧匹配。</summary>
            /// <param name="text">待匹配正文。</param>
            /// <returns>是否至少有一侧匹配。</returns>
            public override bool IsMatch(string text)
            {
                return _left.IsMatch(text) || _right.IsMatch(text);
            }
        }

        #endregion

        #region 递归下降解析器

        /// <summary>按括号、AND、OR 优先级构建表达式语法树。</summary>
        private sealed class ExpressionParser
        {
            private readonly string _expression;
            private int _index;
            private string _errorMessage;
            private int _errorPosition = -1;

            /// <summary>创建解析器。</summary>
            /// <param name="expression">原始表达式。</param>
            public ExpressionParser(string expression)
            {
                _expression = expression;
            }

            /// <summary>解析完整表达式并检查是否存在尾随字符。</summary>
            /// <returns>解析成功或失败的结果。</returns>
            public ParseResult Parse()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    return Fail("筛选表达式不能为空", _index);
                }

                var root = ParseOrExpression();
                if (root == null)
                {
                    return Fail(_errorMessage, _errorPosition);
                }

                SkipWhitespace();
                if (!AtEnd)
                {
                    return Fail("表达式缺少运算符或包含多余内容", _index);
                }

                return new ParseResult(new TextMatcher(root), string.Empty, -1);
            }

            /// <summary>解析 OR 层级，AND 层级优先于当前层级。</summary>
            /// <returns>OR 语法树节点。</returns>
            private ExpressionNode ParseOrExpression()
            {
                var left = ParseAndExpression();
                if (left == null) return null;

                while (TryConsume('|'))
                {
                    var right = ParseAndExpression();
                    if (right == null)
                    {
                        SetError("| 后缺少筛选条件", Math.Min(_index, _expression.Length));
                        return null;
                    }

                    left = new OrNode(left, right);
                }

                return left;
            }

            /// <summary>解析 AND 层级，括号和文本片段位于更高层级。</summary>
            /// <returns>AND 语法树节点。</returns>
            private ExpressionNode ParseAndExpression()
            {
                var left = ParsePrimaryExpression();
                if (left == null) return null;

                while (TryConsume('&'))
                {
                    var right = ParsePrimaryExpression();
                    if (right == null)
                    {
                        SetError("& 后缺少筛选条件", Math.Min(_index, _expression.Length));
                        return null;
                    }

                    left = new AndNode(left, right);
                }

                return left;
            }

            /// <summary>解析括号表达式或一个文本片段。</summary>
            /// <returns>基本语法树节点。</returns>
            private ExpressionNode ParsePrimaryExpression()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    SetError("缺少筛选条件", _index);
                    return null;
                }

                if (TryConsume('('))
                {
                    var nested = ParseOrExpression();
                    if (nested == null) return null;

                    SkipWhitespace();
                    if (!TryConsume(')'))
                    {
                        SetError("缺少右括号 )", _index);
                        return null;
                    }

                    return nested;
                }

                if (Peek() == ')' || Peek() == '&' || Peek() == '|')
                {
                    SetError("运算符前缺少筛选条件", _index);
                    return null;
                }

                var value = ParseValue();
                if (value == null) return null;
                return new ContainsNode(value);
            }

            /// <summary>读取普通或双引号文本，并处理引号内的转义字符。</summary>
            /// <returns>去除外层空白的文本片段；失败时返回 null。</returns>
            private string ParseValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    SetError("缺少筛选条件", _index);
                    return null;
                }

                if (Peek() == '"')
                {
                    return ParseQuotedValue();
                }

                var start = _index;
                while (!AtEnd && Peek() != '&' && Peek() != '|' && Peek() != '(' && Peek() != ')')
                {
                    _index++;
                }

                var value = _expression.Substring(start, _index - start).Trim();
                if (value.Length == 0)
                {
                    SetError("筛选条件不能为空", start);
                    return null;
                }

                return value;
            }

            /// <summary>读取双引号文本，支持 \" 和 \\ 转义。</summary>
            /// <returns>解码后的文本片段；失败时返回 null。</returns>
            private string ParseQuotedValue()
            {
                var quotePosition = _index;
                _index++;
                var value = new System.Text.StringBuilder();
                while (!AtEnd)
                {
                    var current = _expression[_index++];
                    if (current == '"')
                    {
                        if (value.Length == 0)
                        {
                            SetError("引号内的筛选条件不能为空", quotePosition);
                            return null;
                        }

                        return value.ToString();
                    }

                    if (current == '\\')
                    {
                        if (AtEnd)
                        {
                            SetError("引号中的转义字符不完整", _index - 1);
                            return null;
                        }

                        var escaped = _expression[_index++];
                        if (escaped != '"' && escaped != '\\')
                        {
                            SetError("引号中只支持 \\\" 和 \\\\ 转义", _index - 1);
                            return null;
                        }

                        value.Append(escaped);
                        continue;
                    }

                    value.Append(current);
                }

                SetError("缺少右引号 \"", quotePosition);
                return null;
            }

            /// <summary>尝试读取指定符号，并跳过符号两侧的空白。</summary>
            /// <param name="symbol">需要读取的符号。</param>
            /// <returns>是否读取成功。</returns>
            private bool TryConsume(char symbol)
            {
                SkipWhitespace();
                if (AtEnd || Peek() != symbol) return false;

                _index++;
                SkipWhitespace();
                return true;
            }

            /// <summary>跳过当前游标前的空白字符。</summary>
            private void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_expression[_index]))
                {
                    _index++;
                }
            }

            /// <summary>取得当前游标字符。</summary>
            /// <returns>当前字符；到达末尾时返回空字符。</returns>
            private char Peek()
            {
                return AtEnd ? '\0' : _expression[_index];
            }

            /// <summary>设置第一次解析错误，保留最接近根因的位置。</summary>
            /// <param name="message">错误信息。</param>
            /// <param name="position">错误位置。</param>
            private void SetError(string message, int position)
            {
                if (_errorMessage != null) return;
                _errorMessage = message;
                _errorPosition = Math.Max(0, position);
            }

            /// <summary>创建失败结果。</summary>
            /// <param name="message">错误信息。</param>
            /// <param name="position">错误位置。</param>
            /// <returns>失败的解析结果。</returns>
            private ParseResult Fail(string message, int position)
            {
                return new ParseResult(null, message ?? "筛选表达式无效", position);
            }

            /// <summary>当前游标是否位于表达式末尾。</summary>
            private bool AtEnd => _index >= _expression.Length;
        }

        #endregion
    }
}
