using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace WS_Modules.Pooling.Editor
{
    /// <summary>
    /// 扫描项目运行时程序集中的 <see cref="IPoolable"/> 类型，并生成中央 ID Registry 与程序集内注册入口。
    /// </summary>
    public static class ClassPoolPrewarmRegistryGenerator
    {
        #region 常量与路径

        private const string CentralRegistryPath =
            "Assets/Scripts/WSFrame/Pooling/Generated/ClassPoolPrewarmRegistry.generated.cs";
        // 程序集内注册文件的专用后缀，避免误删除手写注册实现。
        private const string RegistrationFileSuffix = ".ClassPoolPrewarmRegistration.generated.cs";
        // 运行时程序集内没有 Assembly Definition 时的默认生成目录。
        private const string AssemblyCSharpGeneratedDirectory = "Assets/Generated/ClassPoolPrewarm";

        #endregion

        #region 公开生成入口

        /// <summary>
        /// 扫描所有可用的运行时程序集并重新生成 Class Pool 预热 Registry。
        /// </summary>
        [MenuItem("WSFrame/Pooling/Generate Class Pool Prewarm Registry", priority = 2000)]
        public static void Generate()
        {
            GenerateAndGetPaths();
        }

        /// <summary>
        /// 扫描所有可用的运行时程序集、重新生成 Class Pool 预热 Registry，并返回实际输出文件路径。
        /// </summary>
        /// <returns>中央 Registry 在首位，其余程序集注册文件按路径排序的只读列表。</returns>
        public static IReadOnlyList<string> GenerateAndGetPaths()
        {
            List<Type> discoveredTypes = TypeCache.GetTypesDerivedFrom<IPoolable>().ToList();
            // 获取 Unity Player 编译目标包含的程序集名称，排除 Editor-only 类型。
            var playerAssemblyNames = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(assembly => assembly.name),
                StringComparer.Ordinal);
            List<Candidate> candidates = discoveredTypes
                .Where(type => IsSelectableType(type, playerAssemblyNames))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(CreateCandidate)
                .ToList();

            // 先验证稳定 ID，再写文件，避免冲突时留下只更新一半的生成结果。
            EnsureNoIdConflicts(candidates);
            WriteCentralRegistry(candidates);
            IReadOnlyList<string> registrationPaths = WriteAssemblyRegistrations(candidates);
            DeleteStaleRegistrationFiles(registrationPaths);
            AssetDatabase.Refresh();

            string exclusionSummary = BuildExclusionSummary(discoveredTypes, playerAssemblyNames);
            string candidateNames = candidates.Count == 0
                ? "<none>"
                : string.Join(", ", candidates.Select(candidate => candidate.Type.FullName));
            string generatedPaths = string.Join(", ", registrationPaths.Prepend(CentralRegistryPath));
            Debug.Log(
                $"Generated class pool prewarm registry. Candidates: {candidates.Count}, " +
                $"Excluded: {discoveredTypes.Count - candidates.Count} ({exclusionSummary}). " +
                $"Types: {candidateNames}. Files: {generatedPaths}");

            return BuildOrderedOutputPaths(registrationPaths);
        }

        /// <summary>
        /// 查询项目中当前存在的 Class Pool Registry 生成文件，不触发扫描或文件写入。
        /// </summary>
        /// <returns>中央 Registry 在首位，其余程序集注册文件按路径排序的只读列表。</returns>
        public static IReadOnlyList<string> GetGeneratedFilePaths()
        {
            string[] registrationPaths = Directory.GetFiles(
                    "Assets",
                    $"*{RegistrationFileSuffix}",
                    SearchOption.AllDirectories)
                .Select(NormalizePath)
                .ToArray();
            return BuildOrderedOutputPaths(registrationPaths);
        }

        #endregion

        #region 菜单入口

        /// <summary>
        /// 从 Project 窗口中选中的预热配置刷新 Registry。
        /// </summary>
        [MenuItem("Assets/WSFrame/Pooling/Refresh Class Pool Prewarm Registry", false, 2000)]
        private static void GenerateFromSelectedPoolPrewarmConfig()
        {
            Generate();
        }

        /// <summary>
        /// 判断当前 Project 选择是否允许显示 Registry 刷新菜单。
        /// </summary>
        /// <returns>选中 PoolPrewarmConfig 时返回 true。</returns>
        [MenuItem("Assets/WSFrame/Pooling/Refresh Class Pool Prewarm Registry", true)]
        private static bool CanGenerateFromSelectedPoolPrewarmConfig()
        {
            return Selection.activeObject is PoolPrewarmConfig;
        }

        /// <summary>
        /// 从 PoolPrewarmConfig Inspector 的上下文菜单刷新 Registry。
        /// </summary>
        /// <param name="command">Unity 传入的上下文菜单命令。</param>
        [MenuItem("CONTEXT/PoolPrewarmConfig/Refresh Class Pool Prewarm Registry")]
        private static void GenerateFromPoolPrewarmConfigContext(MenuCommand command)
        {
            Generate();
        }

        #endregion

        #region 候选类型筛选

        /// <summary>
        /// 判断类型是否满足可由 Class Pool 强类型创建与预热的契约。
        /// </summary>
        /// <param name="type">待检查类型。</param>
        /// <returns>类型可生成注册项时返回 true。</returns>
        private static bool IsSelectableType(Type type, ISet<string> playerAssemblyNames)
        {
            return GetExclusionReason(type, playerAssemblyNames) == null;
        }

        /// <summary>
        /// 返回候选类型不符合 Class Pool 生成契约的具体原因。
        /// </summary>
        /// <param name="type">待检查类型。</param>
        /// <param name="playerAssemblyNames">Unity Player 编译目标包含的程序集名称。</param>
        /// <returns>不符合契约时返回原因；符合时返回 null。</returns>
        private static string GetExclusionReason(Type type, ISet<string> playerAssemblyNames)
        {
            if (type == null)
            {
                return "null type";
            }

            if (!playerAssemblyNames.Contains(type.Assembly.GetName().Name))
            {
                return "Editor-only assembly";
            }

            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                return "MonoBehaviour";
            }

            if (!type.IsClass || type.IsAbstract)
            {
                return "not a concrete class";
            }

            if (type.IsGenericTypeDefinition || type.IsGenericType)
            {
                return "generic type";
            }

            if (!type.IsPublic && !type.IsNestedPublic)
            {
                return "non-public type";
            }

            return type.GetConstructor(Type.EmptyTypes) == null
                ? "missing public parameterless constructor"
                : null;
        }

        /// <summary>
        /// 按原因汇总未进入 Registry 的类型，便于从 Generate 日志直接定位扫描问题。
        /// </summary>
        /// <param name="types">TypeCache 返回的全部派生类型。</param>
        /// <param name="playerAssemblyNames">Unity Player 编译目标包含的程序集名称。</param>
        /// <returns>排除原因与数量摘要；没有排除项时返回 none。</returns>
        private static string BuildExclusionSummary(
            IEnumerable<Type> types,
            ISet<string> playerAssemblyNames)
        {
            string[] groups = types
                .Select(type => GetExclusionReason(type, playerAssemblyNames))
                .Where(reason => reason != null)
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}: {group.Count()}")
                .ToArray();
            return groups.Length == 0 ? "none" : string.Join(", ", groups);
        }

        /// <summary>
        /// 将反射类型转换为生成代码所需的稳定候选数据。
        /// </summary>
        /// <param name="type">已通过契约筛选的类型。</param>
        /// <returns>包含稳定 ID、显示名和程序集信息的候选数据。</returns>
        private static Candidate CreateCandidate(Type type)
        {
            string fullName = type.FullName ?? type.Name;
            int stableId = GetStableId(fullName);
            return new Candidate(
                type,
                stableId,
                $"{SanitizeIdentifier(fullName)}_{stableId:X}",
                $"global::{fullName.Replace("+", ".")}",
                $"{fullName} ({type.Assembly.GetName().Name})",
                type.Assembly.GetName().Name);
        }

        /// <summary>
        /// 验证所有候选类型的稳定 ID 唯一，防止不同类型覆盖同一序列化值。
        /// </summary>
        /// <param name="candidates">全部候选类型。</param>
        /// <exception cref="InvalidOperationException">两个候选类型生成相同稳定 ID 时抛出。</exception>
        private static void EnsureNoIdConflicts(IEnumerable<Candidate> candidates)
        {
            var usedIds = new Dictionary<int, Candidate>();
            foreach (Candidate candidate in candidates)
            {
                if (!usedIds.TryGetValue(candidate.Id, out Candidate existing))
                {
                    usedIds[candidate.Id] = candidate;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Class pool prewarm id conflict: {existing.Type.FullName} and {candidate.Type.FullName} " +
                    $"both use {candidate.Id}.");
            }
        }

        #endregion

        #region 生成文件写入

        /// <summary>
        /// 生成只包含稳定 ID 与注册容器的中央 Registry，避免 Pooling 程序集反向引用业务程序集。
        /// </summary>
        /// <param name="candidates">全部候选类型。</param>
        private static void WriteCentralRegistry(IReadOnlyList<Candidate> candidates)
        {
            var builder = new StringBuilder();
            AppendGeneratedHeader(builder, nameof(ClassPoolPrewarmRegistryGenerator));
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine();
            builder.AppendLine("namespace WS_Modules.Pooling");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>标识可由全局配置预热的 Class Pool 类型。</summary>");
            builder.AppendLine("    public enum ClassPoolPrewarmId");
            builder.AppendLine("    {");
            builder.AppendLine("        /// <summary>未选择任何 Class Pool 类型。</summary>");
            builder.AppendLine("        None = 0,");
            foreach (Candidate candidate in candidates)
            {
                builder.AppendLine($"        /// <summary>{EscapeXml(candidate.DisplayName)}。</summary>");
                builder.AppendLine($"        {candidate.EnumName} = {candidate.Id},");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>标记由生成代码提供的 Class Pool 注册入口。</summary>");
            builder.AppendLine("    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]");
            builder.AppendLine("    public sealed class ClassPoolPrewarmRegistrarAttribute : Attribute");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>保存单个 Class Pool 类型的运行时预热信息。</summary>");
            builder.AppendLine("    public readonly struct ClassPoolPrewarmRegistryEntry");
            builder.AppendLine("    {");
            builder.AppendLine("        /// <summary>初始化注册项。</summary>");
            builder.AppendLine("        /// <param name=\"id\">稳定类型 ID。</param>");
            builder.AppendLine("        /// <param name=\"type\">Class Pool 对象类型。</param>");
            builder.AppendLine("        /// <param name=\"displayName\">Inspector 显示名称。</param>");
            builder.AppendLine("        /// <param name=\"apply\">执行强类型预热的委托。</param>");
            builder.AppendLine("        public ClassPoolPrewarmRegistryEntry(");
            builder.AppendLine("            ClassPoolPrewarmId id,");
            builder.AppendLine("            Type type,");
            builder.AppendLine("            string displayName,");
            builder.AppendLine("            Action<ClassPoolModule, int, int> apply)");
            builder.AppendLine("        {");
            builder.AppendLine("            Id = id;");
            builder.AppendLine("            Type = type;");
            builder.AppendLine("            DisplayName = displayName;");
            builder.AppendLine("            Apply = apply;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>获取稳定类型 ID。</summary>");
            builder.AppendLine("        public ClassPoolPrewarmId Id { get; }");
            builder.AppendLine("        /// <summary>获取 Class Pool 对象类型。</summary>");
            builder.AppendLine("        public Type Type { get; }");
            builder.AppendLine("        /// <summary>获取 Inspector 显示名称。</summary>");
            builder.AppendLine("        public string DisplayName { get; }");
            builder.AppendLine("        /// <summary>获取执行强类型预热的委托。</summary>");
            builder.AppendLine("        public Action<ClassPoolModule, int, int> Apply { get; }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>集中保存各运行时程序集生成的 Class Pool 预热注册项。</summary>");
            builder.AppendLine("    public static class ClassPoolPrewarmRegistry");
            builder.AppendLine("    {");
            builder.AppendLine("        #region 字段与属性");
            builder.AppendLine();
            builder.AppendLine("        private static readonly List<ClassPoolPrewarmRegistryEntry> EntriesValue = new();");
            builder.AppendLine("        private static readonly Dictionary<ClassPoolPrewarmId, ClassPoolPrewarmRegistryEntry> EntryMap = new();");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>获取当前已注册的 Class Pool 条目。</summary>");
            builder.AppendLine("        public static IReadOnlyList<ClassPoolPrewarmRegistryEntry> Entries => EntriesValue;");
            builder.AppendLine();
            builder.AppendLine("        #endregion");
            builder.AppendLine();
            builder.AppendLine("        #region 注册与查询");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>注册一个生成的 Class Pool 预热条目；重复 ID 注册保持幂等。</summary>");
            builder.AppendLine("        /// <param name=\"entry\">待注册条目。</param>");
            builder.AppendLine("        public static void Register(ClassPoolPrewarmRegistryEntry entry)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (entry.Id == ClassPoolPrewarmId.None || EntryMap.ContainsKey(entry.Id))");
            builder.AppendLine("            {");
            builder.AppendLine("                return;");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            // 同步写入查询字典与有序列表，保证 Inspector 和运行时读取同一批条目。");
            builder.AppendLine("            EntryMap.Add(entry.Id, entry);");
            builder.AppendLine("            EntriesValue.Add(entry);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>按稳定 ID 查询 Class Pool 注册项。</summary>");
            builder.AppendLine("        /// <param name=\"id\">稳定类型 ID。</param>");
            builder.AppendLine("        /// <param name=\"entry\">查询成功时返回注册项。</param>");
            builder.AppendLine("        /// <returns>找到注册项时返回 true。</returns>");
            builder.AppendLine("        public static bool TryGetEntry(ClassPoolPrewarmId id, out ClassPoolPrewarmRegistryEntry entry)");
            builder.AppendLine("        {");
            builder.AppendLine("            return EntryMap.TryGetValue(id, out entry);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>获取稳定 ID 对应的 Inspector 显示名称。</summary>");
            builder.AppendLine("        /// <param name=\"id\">稳定类型 ID。</param>");
            builder.AppendLine("        /// <returns>已注册类型的显示名称；未注册时返回空字符串。</returns>");
            builder.AppendLine("        public static string GetDisplayName(ClassPoolPrewarmId id)");
            builder.AppendLine("        {");
            builder.AppendLine("            return TryGetEntry(id, out ClassPoolPrewarmRegistryEntry entry)");
            builder.AppendLine("                ? entry.DisplayName");
            builder.AppendLine("                : string.Empty;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        #endregion");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            WriteGeneratedFile(CentralRegistryPath, builder.ToString());
        }

        /// <summary>
        /// 按程序集生成强类型注册代码，使业务程序集保持单向依赖 Pooling。
        /// </summary>
        /// <param name="candidates">全部候选类型。</param>
        /// <returns>本次生成的程序集注册文件路径。</returns>
        private static IReadOnlyList<string> WriteAssemblyRegistrations(IEnumerable<Candidate> candidates)
        {
            var generatedPaths = new List<string>();
            foreach (IGrouping<string, Candidate> group in candidates.GroupBy(candidate => candidate.AssemblyName))
            {
                string outputPath = GetAssemblyRegistrationPath(group.Key);
                string className = $"{SanitizeIdentifier(group.Key)}ClassPoolPrewarmRegistration";
                var builder = new StringBuilder();
                AppendGeneratedHeader(builder, nameof(ClassPoolPrewarmRegistryGenerator));
                builder.AppendLine("using UnityEngine;");
                builder.AppendLine();
                builder.AppendLine("namespace WS_Modules.Pooling.Generated");
                builder.AppendLine("{");
                builder.AppendLine($"    /// <summary>注册 {EscapeXml(group.Key)} 程序集中的 Class Pool 类型。</summary>");
                builder.AppendLine($"    public static class {className}");
                builder.AppendLine("    {");
                builder.AppendLine("        /// <summary>向中央 Registry 注册本程序集生成的强类型预热委托。</summary>");
                builder.AppendLine("        [ClassPoolPrewarmRegistrar]");
                builder.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]");
                builder.AppendLine("        public static void Register()");
                builder.AppendLine("        {");
                foreach (Candidate candidate in group.OrderBy(candidate => candidate.Type.FullName, StringComparer.Ordinal))
                {
                    builder.AppendLine("            ClassPoolPrewarmRegistry.Register(new ClassPoolPrewarmRegistryEntry(");
                    builder.AppendLine($"                (ClassPoolPrewarmId){candidate.Id},");
                    builder.AppendLine($"                typeof({candidate.TypeReference}),");
                    builder.AppendLine($"                \"{EscapeString(candidate.DisplayName)}\",");
                    builder.AppendLine($"                (module, count, capacity) => module.Prewarm<{candidate.TypeReference}>(count, capacity)));");
                }

                builder.AppendLine("        }");
                builder.AppendLine("    }");
                builder.AppendLine("}");

                WriteGeneratedFile(outputPath, builder.ToString());
                generatedPaths.Add(outputPath);
            }

            return generatedPaths;
        }

        /// <summary>
        /// 统一生成面板和日志使用的输出顺序，保证中央 Registry 始终位于首位。
        /// </summary>
        /// <param name="registrationPaths">各运行时程序集的注册文件路径。</param>
        /// <returns>去重且稳定排序的输出路径列表。</returns>
        private static IReadOnlyList<string> BuildOrderedOutputPaths(IEnumerable<string> registrationPaths)
        {
            var paths = new List<string> { CentralRegistryPath };
            paths.AddRange(registrationPaths
                .Select(NormalizePath)
                .Where(path => !string.Equals(path, CentralRegistryPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            return paths;
        }

        /// <summary>
        /// 删除已不再对应任何候选程序集的旧注册文件，防止过期类型继续注册。
        /// </summary>
        /// <param name="currentPaths">本次仍有效的生成文件路径。</param>
        private static void DeleteStaleRegistrationFiles(IEnumerable<string> currentPaths)
        {
            var currentPathSet = new HashSet<string>(currentPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in Directory.GetFiles("Assets", $"*{RegistrationFileSuffix}", SearchOption.AllDirectories))
            {
                string normalizedPath = NormalizePath(filePath);
                if (!currentPathSet.Contains(normalizedPath))
                {
                    // 只删除带有专用生成后缀的文件，避免触碰任何手写注册实现。
                    AssetDatabase.DeleteAsset(normalizedPath);
                }
            }
        }

        /// <summary>
        /// 解析目标程序集内部的 Generated 输出路径。
        /// </summary>
        /// <param name="assemblyName">目标运行时程序集名称。</param>
        /// <returns>程序集内注册文件的 Unity 资源路径。</returns>
        private static string GetAssemblyRegistrationPath(string assemblyName)
        {
            string assemblyDefinitionPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            string outputDirectory = string.IsNullOrEmpty(assemblyDefinitionPath)
                ? AssemblyCSharpGeneratedDirectory
                : $"{NormalizePath(Path.GetDirectoryName(assemblyDefinitionPath))}/Generated";
            return $"{outputDirectory}/{assemblyName}{RegistrationFileSuffix}";
        }

        /// <summary>
        /// 创建目录并仅在内容变化时写入生成文件，减少无意义的 Unity 重导入。
        /// </summary>
        /// <param name="path">目标 Unity 资源路径。</param>
        /// <param name="content">完整生成源码。</param>
        private static void WriteGeneratedFile(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// 写入统一的自动生成文件头。
        /// </summary>
        /// <param name="builder">目标字符串构建器。</param>
        /// <param name="generatorName">生成器类型名称。</param>
        private static void AppendGeneratedHeader(StringBuilder builder, string generatorName)
        {
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine($"// Generated by {generatorName}.");
            builder.AppendLine("// Do not edit this file manually.");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine();
        }

        #endregion

        #region 文本与 ID 辅助

        /// <summary>
        /// 使用 FNV-1a 从完整类型名生成稳定的正整数 ID。
        /// </summary>
        /// <param name="text">完整类型名。</param>
        /// <returns>非零稳定 ID。</returns>
        private static int GetStableId(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in text)
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                int value = (int)(hash & 0x7fffffff);
                return value == 0 ? 1 : value;
            }
        }

        /// <summary>
        /// 将程序集名或完整类型名转换为合法的 C# 标识符。
        /// </summary>
        /// <param name="text">原始文本。</param>
        /// <returns>可用于生成类型或枚举成员的标识符。</returns>
        private static string SanitizeIdentifier(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            if (builder.Length == 0 || !char.IsLetter(builder[0]) && builder[0] != '_')
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 转义生成的 C# 字符串字面量。
        /// </summary>
        /// <param name="text">原始显示文本。</param>
        /// <returns>已转义文本。</returns>
        private static string EscapeString(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// 转义生成 XML 文档注释中的特殊字符。
        /// </summary>
        /// <param name="text">原始注释文本。</param>
        /// <returns>可安全写入 XML 文档注释的文本。</returns>
        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// 将系统路径标准化为 Unity 使用的正斜杠资源路径。
        /// </summary>
        /// <param name="path">待标准化路径。</param>
        /// <returns>Unity 风格路径。</returns>
        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        #endregion

        #region 嵌套类型

        /// <summary>
        /// 保存单个可生成 Class Pool 类型的稳定元数据。
        /// </summary>
        private sealed class Candidate
        {
            /// <summary>
            /// 初始化候选类型元数据。
            /// </summary>
            /// <param name="type">反射类型。</param>
            /// <param name="id">稳定 ID。</param>
            /// <param name="enumName">中央枚举成员名。</param>
            /// <param name="typeReference">生成代码使用的全局类型引用。</param>
            /// <param name="displayName">Inspector 显示名。</param>
            /// <param name="assemblyName">所属运行时程序集名称。</param>
            public Candidate(
                Type type,
                int id,
                string enumName,
                string typeReference,
                string displayName,
                string assemblyName)
            {
                Type = type;
                Id = id;
                EnumName = enumName;
                TypeReference = typeReference;
                DisplayName = displayName;
                AssemblyName = assemblyName;
            }

            /// <summary>获取反射类型。</summary>
            public Type Type { get; }

            /// <summary>获取稳定 ID。</summary>
            public int Id { get; }

            /// <summary>获取中央枚举成员名。</summary>
            public string EnumName { get; }

            /// <summary>获取生成代码使用的全局类型引用。</summary>
            public string TypeReference { get; }

            /// <summary>获取 Inspector 显示名。</summary>
            public string DisplayName { get; }

            /// <summary>获取所属运行时程序集名称。</summary>
            public string AssemblyName { get; }
        }

        #endregion
    }
}
