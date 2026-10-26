using System;
using System.Collections.Generic;
using System.Text;

namespace DouyinUnity6000Compat
{
    /// @框架补丁计划
    public sealed class FrameworkPatch
    {
        public string Text;
        public bool AccessChanged;
        public bool ExitChanged;
    }

    /// @抖音框架结构兼容核心
    /// <summary>仅在内存中处理已知 Unity 6000 framework 形态，不读写文件或调用 Unity。</summary>
    public static class DouyinCompatCore
    {
        private const string AccessMarker = "/* GFB_DOUYIN_UNITY6_ACCESS_COMPAT */";
        private const string ExitMarker = "/* GFB_DOUYIN_UNITY6_IMPLICIT_EXIT_COMPAT */";
        private const string OriginalExitName = "GfbDouyinOriginalExitJS";

        // 与 DouyinUnity6FileAccess.jslib 相同：不吞 FS 异常，不伪造权限成功。
        private const string AccessHelper = @"doAccess: function (path, amode) {
    if (amode & ~7) {
      return -28; // EINVAL
    }
    var lookup = FS.lookupPath(path, { follow: true });
    var node = lookup.node;
    if (!node) {
      return -44; // ENOENT
    }
    var perms = '';
    if (amode & 4) perms += 'r';
    if (amode & 2) perms += 'w';
    if (amode & 1) perms += 'x';
    if (perms && FS.nodePermissions(node, perms)) {
      return -2; // EACCES
    }
    return 0;
  }";

        // 原 exitJS 只改声明名称；原正文、注释和 _proc_exit 等依赖引用全部保留。
        private const string ExitWrapper = @"function exitJS(status, implicit) {
    if (implicit === true && status === 0 && keepRuntimeAlive() &&
        !(typeof ENVIRONMENT_IS_PTHREAD !== 'undefined' && ENVIRONMENT_IS_PTHREAD) &&
        typeof GameGlobal === 'object' && GameGlobal && GameGlobal.unityNamespace &&
        GameGlobal.unityNamespace.unityVersion === '6000.0.46f1' && Module['StarkSYSCALLS']) {
      EXITSTATUS = status;
      return;
    }
    return GfbDouyinOriginalExitJS(status, implicit);
  }";

        /// @规划框架补丁
        public static FrameworkPatch Plan(string source)
        {
            if (string.IsNullOrEmpty(source))
                throw Unknown("framework 内容为空");

            List<Token> tokens = Tokenize(source);
            var edits = new List<Edit>();
            int objectOpen = FindSyscalls(tokens);
            int objectClose = FindClosing(tokens, objectOpen);
            bool accessChanged = !HasAccessHelper(tokens, objectOpen, objectClose);
            if (accessChanged)
            {
                string comma = objectClose == objectOpen + 1 ? "" : ",";
                edits.Add(new Edit(tokens[objectOpen].End, 0,
                    "\n" + AccessMarker + "\n" + AccessHelper + comma + "\n"));
            }

            Function exit = FindFunction(tokens, "exitJS", true);
            Function original = FindFunction(tokens, OriginalExitName, false);
            bool exitChanged = original == null;
            if (exitChanged)
            {
                // 同名变量或不完整旧补丁不能作为已兼容的依据，也不能被新声明覆盖。
                foreach (Token token in tokens)
                    if (Is(token, OriginalExitName))
                        throw Unknown("原 exitJS 保留名称已被其他代码占用");
                RequireOriginalExit(tokens, exit);
                Token name = tokens[exit.Name];
                edits.Add(new Edit(name.Start, name.End - name.Start, OriginalExitName));
                edits.Add(new Edit(tokens[exit.Close].End, 0,
                    "\n" + ExitMarker + "\n" + ExitWrapper + "\n"));
            }
            else
            {
                RequireOriginalExit(tokens, original);
                // 同时识别 jslib 链接输出和本核心输出，绝不只凭全局标记跳过。
                if (!SameFunction(tokens, exit, Tokenize(ExitWrapper)))
                {
                    string legacy = ExitWrapper.Replace("GameGlobal.unityNamespace.unityVersion === '6000.0.46f1'", @"/^6000\./.test(GameGlobal.unityNamespace.unityVersion)");
                    if (!SameFunction(tokens, exit, Tokenize(legacy)))
                        throw Unknown("exitJS 已有保留函数，但包装结构不是已知窄范围兼容实现");
                    edits.Add(new Edit(tokens[exit.Start].Start, tokens[exit.Close].End - tokens[exit.Start].Start, ExitWrapper));
                    exitChanged = true;
                }
            }

            // 所有结构验证完成后才在本地字符串上合成；失败不返回半份计划。
            edits.Sort(delegate(Edit left, Edit right) { return right.Start.CompareTo(left.Start); });
            string text = source;
            foreach (Edit edit in edits)
                text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
            return new FrameworkPatch { Text = text, AccessChanged = accessChanged, ExitChanged = exitChanged };
        }

        private static int FindSyscalls(List<Token> tokens)
        {
            int found = -1;
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!Is(tokens[i], "var") || !Is(tokens[i + 1], "SYSCALLS")) continue;
                int at = i + 2;
                Expect(tokens, ref at, "=");
                if (At(tokens, at, "Module"))
                {
                    Expect(tokens, ref at, "Module");
                    Expect(tokens, ref at, ".");
                    Expect(tokens, ref at, "StarkSYSCALLS");
                    Expect(tokens, ref at, "=");
                }
                if (!At(tokens, at, "{") || found >= 0)
                    throw Unknown("SYSCALLS 不是唯一的已知对象声明");
                found = at;
            }
            if (found < 0) throw Unknown("找不到已知 SYSCALLS 对象声明");
            return found;
        }

        private static bool HasAccessHelper(List<Token> tokens, int open, int close)
        {
            bool propertyStart = true;
            for (int i = open + 1; i < close; i++)
            {
                Token token = tokens[i];
                if (propertyStart)
                {
                    // 仅检查 SYSCALLS 自身的属性；保留自定义 helper、带引号属性和方法。
                    if (IsProperty(token, "doAccess")) return true;
                    if (Is(token, "["))
                    {
                        int end = FindClosing(tokens, i);
                        if (end == i + 2 && IsProperty(tokens[i + 1], "doAccess")) return true;
                        throw Unknown("SYSCALLS 含未知计算属性，不能断定是否已有 doAccess");
                    }
                    if (Is(token, ".") || (token.Kind == 1 && token.Text.IndexOf('\\') >= 0))
                        throw Unknown("SYSCALLS 含展开或转义属性，不能猜测 helper");
                    if ((Is(token, "get") || Is(token, "set")) &&
                        i + 1 < close && IsProperty(tokens[i + 1], "doAccess")) return true;
                    propertyStart = false;
                }
                if (Is(token, "{") || Is(token, "[") || Is(token, "("))
                    i = FindClosing(tokens, i);
                else if (Is(token, ",")) propertyStart = true;
            }
            return false;
        }

        private static bool IsProperty(Token token, string name)
        {
            return Is(token, name) || (token.Kind == 1 &&
                (token.Text == "'" + name + "'" || token.Text == "\"" + name + "\""));
        }

        private static Function FindFunction(List<Token> tokens, string name, bool required)
        {
            Function found = null;
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!Is(tokens[i], "function") || !Is(tokens[i + 1], name)) continue;
                if (found != null) throw Unknown(name + " 存在多个声明");
                int at = i + 2;
                Expect(tokens, ref at, "(");
                Expect(tokens, ref at, "status");
                Expect(tokens, ref at, ",");
                Expect(tokens, ref at, "implicit");
                Expect(tokens, ref at, ")");
                if (!At(tokens, at, "{")) throw Unknown(name + " 的函数体结构不符");
                found = new Function { Start = i, Name = i + 1, Open = at, Close = FindClosing(tokens, at) };
            }
            if (found == null && required) throw Unknown("找不到唯一的已知 " + name + " 函数");
            return found;
        }

        private static void RequireOriginalExit(List<Token> tokens, Function function)
        {
            int at = function.Open + 1;
            foreach (string expected in new[] { "EXITSTATUS", "=", "status", ";", "_proc_exit", "(", "status", ")" })
                Expect(tokens, ref at, expected);
            if (At(tokens, at, ";")) at++;
            if (at != function.Close)
                throw Unknown("exitJS 原实现不是已知 EXITSTATUS/_proc_exit 结构，不能猜测或吞掉真实退出");
        }

        private static bool SameFunction(List<Token> tokens, Function function, List<Token> expected)
        {
            return Canonical(tokens, function.Start, function.Close + 1) == Canonical(expected, 0, expected.Count);
        }

        private static string Canonical(List<Token> tokens, int start, int end)
        {
            var result = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                Token token = tokens[i];
                // 固定版压缩器会省略块尾分号；除此之外不放宽控制流或退出条件。
                if (Is(token, ";") && i + 1 < end && Is(tokens[i + 1], "}")) continue;
                string value = token.Kind == 1 ? token.Text.Substring(1, token.Text.Length - 2) : token.Text;
                result.Append(token.Kind).Append(':').Append(value.Length).Append(':').Append(value).Append('|');
            }
            return result.ToString();
        }

        private static int FindClosing(List<Token> tokens, int open)
        {
            var stack = new Stack<string>();
            for (int i = open; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                if (token.Kind != 0) continue;
                string value = token.Text;
                if (value == "{" || value == "(" || value == "[") stack.Push(value);
                else if (value == "}" || value == ")" || value == "]")
                {
                    if (stack.Count == 0) throw Unknown("JavaScript 对象或函数边界不完整");
                    string left = stack.Pop();
                    if (!((left == "{" && value == "}") || (left == "(" && value == ")") ||
                        (left == "[" && value == "]"))) throw Unknown("JavaScript 括号结构不匹配");
                    if (stack.Count == 0) return i;
                }
            }
            throw Unknown("无法确定 SYSCALLS 或 exitJS 的完整边界");
        }

        // 小型只读词法定位器，不重写 JS。注释、字符串、模板和正则不参与名称及括号识别。
        // 仅面向本固定版生成器；不作为任意 JavaScript 的语法校验器。
        private static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            int at = 0;
            ReadTokens(source, ref at, tokens, false);
            return tokens;
        }

        private static void ReadTokens(string source, ref int at, List<Token> tokens, bool interpolation)
        {
            int braceDepth = 0;
            while (at < source.Length)
            {
                char c = source[at];
                if (char.IsWhiteSpace(c) || c == '\uFEFF') { at++; continue; }
                if (c == '/' && at + 1 < source.Length && source[at + 1] == '/')
                {
                    at += 2;
                    while (at < source.Length && !IsLineEnd(source[at])) at++;
                    continue;
                }
                if (c == '/' && at + 1 < source.Length && source[at + 1] == '*')
                {
                    int end = source.IndexOf("*/", at + 2, StringComparison.Ordinal);
                    if (end < 0) throw Unknown("JavaScript 块注释未闭合");
                    at = end + 2;
                    continue;
                }
                int start = at;
                int kind = 0;
                if (c == '`')
                {
                    kind = 2;
                    SkipTemplate(source, ref at);
                }
                else if (c == '\'' || c == '"')
                {
                    kind = 1;
                    at++;
                    bool closed = false;
                    while (at < source.Length)
                    {
                        char next = source[at++];
                        if (next == '\\')
                        {
                            if (at < source.Length && source[at] == '\r' && at + 1 < source.Length && source[at + 1] == '\n') at++;
                            at++;
                        }
                        else if (next == c) { closed = true; break; }
                        else if (kind == 1 && IsLineEnd(next)) throw Unknown("JavaScript 字符串未闭合");
                    }
                    if (!closed) throw Unknown("JavaScript 字符串或模板未闭合");
                }
                else if (c == '/' && RegexMayStart(tokens))
                {
                    kind = 3;
                    at++;
                    bool inClass = false;
                    bool closed = false;
                    while (at < source.Length)
                    {
                        char next = source[at++];
                        if (IsLineEnd(next)) throw Unknown("JavaScript 正则表达式未闭合");
                        if (next == '\\') at++;
                        else if (next == '[') inClass = true;
                        else if (next == ']') inClass = false;
                        else if (next == '/' && !inClass) { closed = true; break; }
                    }
                    if (!closed) throw Unknown("JavaScript 正则表达式未闭合");
                    while (at < source.Length && char.IsLetter(source[at])) at++;
                }
                else if (IsWord(c))
                {
                    at++;
                    while (at < source.Length && IsWord(source[at])) at++;
                }
                else
                {
                    at++;
                    if ((c == '+' || c == '-') && at < source.Length && source[at] == c) at++;
                }
                if (at > source.Length) throw Unknown("JavaScript 转义或字面量不完整");
                var token = new Token { Start = start, End = at, Text = source.Substring(start, at - start), Kind = kind };
                if (interpolation && Is(token, "}"))
                {
                    if (braceDepth == 0) return;
                    braceDepth--;
                }
                else if (interpolation && Is(token, "{")) braceDepth++;
                tokens.Add(token);
            }
            if (interpolation) throw Unknown("JavaScript 模板插值未闭合");
        }

        private static void SkipTemplate(string source, ref int at)
        {
            at++; // 开始反引号。
            while (at < source.Length)
            {
                char next = source[at++];
                if (next == '\\')
                {
                    if (at >= source.Length) throw Unknown("JavaScript 模板转义不完整");
                    at++;
                }
                else if (next == '`') return;
                else if (next == '$' && at < source.Length && source[at] == '{')
                {
                    at++;
                    // 插值中可以包含字符串、对象或嵌套模板，但都不是目标框架声明。
                    ReadTokens(source, ref at, new List<Token>(), true);
                }
            }
            throw Unknown("JavaScript 模板未闭合");
        }

        private static bool RegexMayStart(List<Token> tokens)
        {
            if (tokens.Count == 0) return true;
            Token last = tokens[tokens.Count - 1];
            if (last.Kind != 0) return false;
            if (last.Text == ")")
            {
                // if/while 等控制语句后的正则语句不能被误认为除法或待修补的源码。
                int depth = 1;
                for (int i = tokens.Count - 2; i >= 0; i--)
                {
                    if (Is(tokens[i], ")")) depth++;
                    else if (Is(tokens[i], "(") && --depth == 0)
                    {
                        if (i == 0) return false;
                        string before = tokens[i - 1].Text;
                        return before == "if" || before == "while" || before == "for" ||
                            before == "with" || before == "switch" || before == "catch";
                    }
                }
                return false;
            }
            switch (last.Text)
            {
                case "(": case "[": case "{": case "}": case "=": case ":": case ",": case ";":
                case "!": case "?": case "&": case "|": case "+": case "-": case "*":
                case "%": case "^": case "~": case "<": case ">": case "return":
                case "throw": case "case": case "delete": case "void": case "typeof":
                case "in": case "of": case "yield": case "await": case "else": case "do":
                    return true;
                default: return false;
            }
        }

        private static bool IsWord(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        private static bool IsLineEnd(char c)
        {
            return c == '\r' || c == '\n' || c == '\u2028' || c == '\u2029';
        }

        private static bool Is(Token token, string text)
        {
            return token.Kind == 0 && token.Text == text;
        }

        private static bool At(List<Token> tokens, int at, string text)
        {
            return at < tokens.Count && Is(tokens[at], text);
        }

        private static void Expect(List<Token> tokens, ref int at, string text)
        {
            if (!At(tokens, at, text)) throw Unknown("预期的已知结构缺少 " + text);
            at++;
        }

        private static InvalidOperationException Unknown(string reason)
        {
            return new InvalidOperationException("抖音 Unity6000 framework 结构未知，已停止修补：" + reason + "。");
        }

        private sealed class Token
        {
            internal int Start, End, Kind;
            internal string Text;
        }

        private sealed class Function
        {
            internal int Start, Name, Open, Close;
        }

        private sealed class Edit
        {
            internal readonly int Start, Length;
            internal readonly string Text;
            internal Edit(int start, int length, string text) { Start = start; Length = length; Text = text; }
        }
    }
}
