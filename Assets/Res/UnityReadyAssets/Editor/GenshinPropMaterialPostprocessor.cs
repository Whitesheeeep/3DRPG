#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenshinProps.Editor
{
    /// <summary>
    /// 根据 Blender 导出清单配置贴图、创建 URP/Lit 材质，并把材质映射回对应 FBX。
    /// </summary>
    internal sealed class GenshinPropMaterialPostprocessor : AssetPostprocessor
    {
        #region 常量与状态

        private const string ManifestFileName = "asset-manifest.json";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string SynchronizeSessionKey = "GenshinProps.Editor.SynchronizeScheduled";

        // 状态：防止材质同步触发的二次导入重复排队，实际资源状态仍通过幂等检查判断。
        private static bool isSynchronizing;

        #endregion

        #region Unity 导入生命周期

        /// <summary>
        /// 在脚本编译或 Domain Reload 后主动安排一次同步，覆盖贴图先于后处理器编译完成的首次导入时序。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ScheduleSynchronizationAfterDomainReload()
        {
            if (SessionState.GetBool(SynchronizeSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SynchronizeSessionKey, true);
            EditorApplication.delayCall += SynchronizeAllPackages;
        }

        /// <summary>
        /// 在贴图导入前根据清单用途配置颜色空间和法线类型。
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (!TryLoadManifestForAsset(assetPath, out ManifestContext context))
            {
                return;
            }

            string relativePath = GetRelativePackagePath(context.PackageRootAssetPath, assetPath);
            TextureRecord texture = context.Manifest.textures.FirstOrDefault(
                item => PathsEqual(item.path, relativePath));
            if (texture == null)
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;
            ApplyTextureSettings(importer, texture);
        }

        /// <summary>
        /// 监听资源导入完成事件，并把同步操作延迟到当前 AssetDatabase 批次结束后执行。
        /// </summary>
        /// <param name="importedAssets">本批次导入的资源路径。</param>
        /// <param name="deletedAssets">本批次删除的资源路径。</param>
        /// <param name="movedAssets">本批次移动后的资源路径。</param>
        /// <param name="movedFromAssetPaths">本批次移动前的资源路径。</param>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (isSynchronizing || !ContainsRelevantAsset(importedAssets, movedAssets))
            {
                return;
            }

            // AssetDatabase 回调期间不创建材质或重导模型，延迟到编辑器空闲阶段统一完成。
            if (SessionState.GetBool(SynchronizeSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SynchronizeSessionKey, true);
            EditorApplication.delayCall += SynchronizeAllPackages;
        }

        #endregion

        #region 资源同步

        /// <summary>
        /// 查找项目内所有导出清单，并依次同步其材质与模型映射。
        /// </summary>
        private static void SynchronizeAllPackages()
        {
            SessionState.SetBool(SynchronizeSessionKey, false);
            if (isSynchronizing)
            {
                return;
            }

            isSynchronizing = true;
            try
            {
                string[] manifestGuids = AssetDatabase.FindAssets($"{Path.GetFileNameWithoutExtension(ManifestFileName)} t:TextAsset");
                foreach (string guid in manifestGuids)
                {
                    string manifestAssetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.Equals(Path.GetFileName(manifestAssetPath), ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ManifestContext context = LoadManifest(manifestAssetPath);
                    SynchronizePackage(context);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                isSynchronizing = false;
            }
        }

        /// <summary>
        /// 创建或更新一个包中的材质，然后为每个 FBX 写入外部材质重映射。
        /// </summary>
        /// <param name="context">清单及其资源根目录。</param>
        private static void SynchronizePackage(ManifestContext context)
        {
            Shader shader = Shader.Find(UrpLitShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"找不到 {UrpLitShaderName}，请先为项目安装并启用 URP。");
            }

            // 首次复制资源时贴图可能早于本脚本完成编译，因此同步阶段再次校正并按需重导贴图。
            SynchronizeTextureImporters(context);

            Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (MaterialRecord record in context.Manifest.materials)
            {
                Material material = CreateOrUpdateMaterial(context, shader, record);
                materials.Add(record.sourceName, material);
            }

            AssetDatabase.SaveAssets();

            // 材质必须先落盘，ModelImporter 才能持久化 SourceAssetIdentifier 到外部材质的映射。
            foreach (AssetRecord asset in context.Manifest.assets)
            {
                ApplyModelMaterialRemaps(context, asset, materials);
            }
        }

        /// <summary>
        /// 创建新的 URP 材质，或幂等更新已有材质的贴图与关键字。
        /// </summary>
        /// <param name="context">清单及其资源根目录。</param>
        /// <param name="shader">URP/Lit Shader。</param>
        /// <param name="record">源材质转换记录。</param>
        /// <returns>可用于 FBX 重映射的持久化材质资源。</returns>
        private static Material CreateOrUpdateMaterial(
            ManifestContext context,
            Shader shader,
            MaterialRecord record)
        {
            string materialsFolder = CombineAssetPath(context.PackageRootAssetPath, "Materials");
            EnsureAssetFolder(materialsFolder);
            string materialPath = CombineAssetPath(materialsFolder, record.outputName + ".mat");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = record.outputName
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            ConfigureUrpMaterial(context, material, record);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 按标准 URP/Lit Specular Workflow 设置贴图、透明裁剪与发光状态。
        /// </summary>
        /// <param name="context">清单及其资源根目录。</param>
        /// <param name="material">待更新的 Unity 材质。</param>
        /// <param name="record">源材质转换记录。</param>
        private static void ConfigureUrpMaterial(
            ManifestContext context,
            Material material,
            MaterialRecord record)
        {
            Texture2D baseTexture = LoadTexture(context, record.baseTexture);
            Texture2D normalTexture = LoadTexture(context, record.normalTexture);
            Texture2D specGlossTexture = LoadTexture(context, record.specGlossTexture);
            Texture2D emissionTexture = LoadTexture(context, record.emissionTexture);

            material.SetFloat("_WorkflowMode", 0f);
            material.EnableKeyword("_SPECULAR_SETUP");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseTexture);
            material.SetTexture("_BumpMap", normalTexture);
            material.SetFloat("_BumpScale", 1f);
            SetKeyword(material, "_NORMALMAP", normalTexture != null);

            material.SetTexture("_SpecGlossMap", specGlossTexture);
            material.SetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f, 1f));
            material.SetFloat("_Smoothness", specGlossTexture != null ? 1f : 0.5f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            SetKeyword(material, "_SPECGLOSSMAP", specGlossTexture != null);

            bool hasEmission = emissionTexture != null;
            material.SetTexture("_EmissionMap", emissionTexture);
            material.SetColor("_EmissionColor", hasEmission
                ? Color.white * Mathf.Max(1f, record.emissionStrength)
                : Color.black);
            SetKeyword(material, "_EMISSION", hasEmission);
            material.globalIlluminationFlags = hasEmission
                ? MaterialGlobalIlluminationFlags.BakedEmissive
                : MaterialGlobalIlluminationFlags.None;

            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", record.alphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", record.cutoff);
            SetKeyword(material, "_ALPHATEST_ON", record.alphaClip);
            material.SetOverrideTag("RenderType", record.alphaClip ? "TransparentCutout" : "Opaque");
            material.renderQueue = record.alphaClip
                ? (int)RenderQueue.AlphaTest
                : (int)RenderQueue.Geometry;
        }

        /// <summary>
        /// 为一个 FBX 配置材质导入方式和源材质名到外部材质的持久映射。
        /// </summary>
        /// <param name="context">清单及其资源根目录。</param>
        /// <param name="asset">待处理的模型记录。</param>
        /// <param name="materials">按 Blender 源材质名索引的 Unity 材质。</param>
        private static void ApplyModelMaterialRemaps(
            ManifestContext context,
            AssetRecord asset,
            IReadOnlyDictionary<string, Material> materials)
        {
            string fbxAssetPath = CombineAssetPath(context.PackageRootAssetPath, asset.fbxPath);
            ModelImporter importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[GenshinProps] 找不到模型导入器：{fbxAssetPath}");
                return;
            }

            bool changed = false;
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                changed = true;
            }

            IDictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> currentMap = importer.GetExternalObjectMap();
            foreach (string sourceMaterialName in asset.materials)
            {
                if (!materials.TryGetValue(sourceMaterialName, out Material material))
                {
                    Debug.LogWarning($"[GenshinProps] {asset.sourceName} 缺少材质：{sourceMaterialName}");
                    continue;
                }

                AssetImporter.SourceAssetIdentifier identifier =
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterialName);
                if (currentMap.TryGetValue(identifier, out UnityEngine.Object mapped) && mapped == material)
                {
                    continue;
                }

                importer.AddRemap(identifier, material);
                changed = true;
            }

            if (changed)
            {
                // 仅当导入设置或映射变化时重导，防止 OnPostprocessAllAssets 形成循环。
                importer.SaveAndReimport();
            }
        }

        #endregion

        #region 贴图设置

        /// <summary>
        /// 检查包内全部贴图的导入设置，只重导与清单用途不一致的贴图。
        /// </summary>
        /// <param name="context">清单及其资源根目录。</param>
        private static void SynchronizeTextureImporters(ManifestContext context)
        {
            foreach (TextureRecord record in context.Manifest.textures)
            {
                string textureAssetPath = CombineAssetPath(context.PackageRootAssetPath, record.path);
                TextureImporter importer = AssetImporter.GetAtPath(textureAssetPath) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[GenshinProps] 找不到贴图导入器：{textureAssetPath}");
                    continue;
                }

                TextureImporterType previousType = importer.textureType;
                bool previousSrgb = importer.sRGBTexture;
                TextureImporterAlphaSource previousAlphaSource = importer.alphaSource;
                bool previousAlphaTransparency = importer.alphaIsTransparency;
                ApplyTextureSettings(importer, record);

                bool changed = previousType != importer.textureType ||
                               previousSrgb != importer.sRGBTexture ||
                               previousAlphaSource != importer.alphaSource ||
                               previousAlphaTransparency != importer.alphaIsTransparency;
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// 根据贴图用途设置 TextureImporter；同一源图若承担多种用途，以 Normal 和线性数据优先。
        /// </summary>
        /// <param name="importer">当前贴图导入器。</param>
        /// <param name="record">贴图用途记录。</param>
        private static void ApplyTextureSettings(TextureImporter importer, TextureRecord record)
        {
            bool isNormal = record.kinds.Contains("normal");
            bool isLinear = isNormal || record.kinds.Contains("smbe") || record.kinds.Contains("specGloss");
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isLinear;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = record.kinds.Contains("base");
        }

        #endregion

        #region 清单与路径

        /// <summary>
        /// 判断本批导入资源是否可能属于该转换包。
        /// </summary>
        /// <param name="importedAssets">导入资源路径。</param>
        /// <param name="movedAssets">移动后资源路径。</param>
        /// <returns>包含清单、FBX 或贴图时返回 true。</returns>
        private static bool ContainsRelevantAsset(string[] importedAssets, string[] movedAssets)
        {
            return importedAssets.Concat(movedAssets).Any(path =>
                string.Equals(Path.GetFileName(path), ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 从当前资源路径向上查找同一转换包的清单。
        /// </summary>
        /// <param name="currentAssetPath">当前 Unity 资源路径。</param>
        /// <param name="context">找到的清单上下文。</param>
        /// <returns>当前资源属于有效转换包时返回 true。</returns>
        private static bool TryLoadManifestForAsset(string currentAssetPath, out ManifestContext context)
        {
            string directory = Path.GetDirectoryName(currentAssetPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(directory) && directory.StartsWith("Assets", StringComparison.Ordinal))
            {
                string candidate = CombineAssetPath(directory, ManifestFileName);
                if (File.Exists(ToAbsoluteProjectPath(candidate)))
                {
                    context = LoadManifest(candidate);
                    return true;
                }

                string parent = Path.GetDirectoryName(directory)?.Replace('\\', '/');
                if (string.Equals(parent, directory, StringComparison.Ordinal))
                {
                    break;
                }

                directory = parent;
            }

            context = null;
            return false;
        }

        /// <summary>
        /// 从磁盘读取并反序列化一个资产清单。
        /// </summary>
        /// <param name="manifestAssetPath">清单的 Unity 资源路径。</param>
        /// <returns>包含清单与包根目录的上下文。</returns>
        private static ManifestContext LoadManifest(string manifestAssetPath)
        {
            string json = File.ReadAllText(ToAbsoluteProjectPath(manifestAssetPath));
            AssetManifest manifest = JsonUtility.FromJson<AssetManifest>(json);
            if (manifest == null || manifest.formatVersion != 1)
            {
                throw new InvalidDataException($"不支持的原神道具清单：{manifestAssetPath}");
            }

            return new ManifestContext
            {
                ManifestAssetPath = manifestAssetPath,
                PackageRootAssetPath = Path.GetDirectoryName(manifestAssetPath)?.Replace('\\', '/'),
                Manifest = manifest
            };
        }

        /// <summary>
        /// 将包内相对路径转换为 Unity 资源路径并加载贴图。
        /// </summary>
        /// <param name="context">清单上下文。</param>
        /// <param name="relativePath">清单中的包内相对路径。</param>
        /// <returns>已导入贴图；路径为空时返回 null。</returns>
        private static Texture2D LoadTexture(ManifestContext context, string relativePath)
        {
            return string.IsNullOrEmpty(relativePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(CombineAssetPath(context.PackageRootAssetPath, relativePath));
        }

        /// <summary>
        /// 拼接并规范化 Unity 资源路径。
        /// </summary>
        /// <param name="left">左侧路径。</param>
        /// <param name="right">右侧路径。</param>
        /// <returns>使用正斜杠的资源路径。</returns>
        private static string CombineAssetPath(string left, string right)
        {
            return (left.TrimEnd('/', '\\') + "/" + right.TrimStart('/', '\\')).Replace('\\', '/');
        }

        /// <summary>
        /// 取得资源相对于转换包根目录的路径。
        /// </summary>
        /// <param name="packageRoot">转换包根资源路径。</param>
        /// <param name="fullAssetPath">完整 Unity 资源路径。</param>
        /// <returns>包内相对路径。</returns>
        private static string GetRelativePackagePath(string packageRoot, string fullAssetPath)
        {
            return fullAssetPath.Substring(packageRoot.TrimEnd('/').Length + 1).Replace('\\', '/');
        }

        /// <summary>
        /// 将 Unity 资源路径转换为当前项目中的绝对磁盘路径。
        /// </summary>
        /// <param name="assetPath">Unity 资源路径。</param>
        /// <returns>绝对磁盘路径。</returns>
        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("无法确定 Unity 项目根目录。");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        /// <summary>
        /// 确保 Unity 资源文件夹存在。
        /// </summary>
        /// <param name="folderAssetPath">待创建的资源文件夹路径。</param>
        private static void EnsureAssetFolder(string folderAssetPath)
        {
            if (AssetDatabase.IsValidFolder(folderAssetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderAssetPath)?.Replace('\\', '/');
            string name = Path.GetFileName(folderAssetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException($"无效的 Unity 资源文件夹：{folderAssetPath}");
            }

            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>
        /// 比较两个清单相对路径，忽略斜杠方向与大小写。
        /// </summary>
        /// <param name="left">第一个路径。</param>
        /// <param name="right">第二个路径。</param>
        /// <returns>路径指向同一清单项时返回 true。</returns>
        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                left?.Replace('\\', '/'),
                right?.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 设置材质 Shader Keyword。
        /// </summary>
        /// <param name="material">目标材质。</param>
        /// <param name="keyword">Shader Keyword。</param>
        /// <param name="enabled">是否启用。</param>
        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        #endregion

        #region 清单数据结构

        /// <summary>
        /// 保存已加载清单及其 Unity 包根路径。
        /// </summary>
        private sealed class ManifestContext
        {
            /// <summary>清单自身的 Unity 资源路径。</summary>
            public string ManifestAssetPath;

            /// <summary>包含 Models、Textures 与 Materials 的包根路径。</summary>
            public string PackageRootAssetPath;

            /// <summary>反序列化后的清单数据。</summary>
            public AssetManifest Manifest;
        }

        /// <summary>
        /// Blender 导出器生成的顶层资产清单。
        /// </summary>
        [Serializable]
        private sealed class AssetManifest
        {
            /// <summary>清单格式版本。</summary>
            public int formatVersion;

            /// <summary>源 Blender 文件路径，仅用于追踪。</summary>
            public string sourceBlend;

            /// <summary>执行导出的 Blender 版本。</summary>
            public string blenderVersion;

            /// <summary>按根对象拆分的模型记录。</summary>
            public AssetRecord[] assets = Array.Empty<AssetRecord>();

            /// <summary>共享材质记录。</summary>
            public MaterialRecord[] materials = Array.Empty<MaterialRecord>();

            /// <summary>贴图导入用途记录。</summary>
            public TextureRecord[] textures = Array.Empty<TextureRecord>();
        }

        /// <summary>
        /// 一个根对象对应的 FBX 和材质槽记录。
        /// </summary>
        [Serializable]
        private sealed class AssetRecord
        {
            /// <summary>Blender 根对象名称。</summary>
            public string sourceName;

            /// <summary>安全化后的输出名称。</summary>
            public string outputName;

            /// <summary>相对于包根目录的 FBX 路径。</summary>
            public string fbxPath;

            /// <summary>根对象类型。</summary>
            public string rootType;

            /// <summary>导出的对象数量。</summary>
            public int objectCount;

            /// <summary>导出的 Mesh 数量。</summary>
            public int meshCount;

            /// <summary>没有 UV 的 Mesh 名称。</summary>
            public string[] uvlessMeshes = Array.Empty<string>();

            /// <summary>FBX 使用的 Blender 源材质名称。</summary>
            public string[] materials = Array.Empty<string>();
        }

        /// <summary>
        /// 一个 Blender 材质转换为 URP/Lit 所需的数据。
        /// </summary>
        [Serializable]
        private sealed class MaterialRecord
        {
            /// <summary>FBX 材质槽中的源名称。</summary>
            public string sourceName;

            /// <summary>Unity 材质资源名称。</summary>
            public string outputName;

            /// <summary>Base Map 相对路径。</summary>
            public string baseTexture;

            /// <summary>Normal Map 相对路径。</summary>
            public string normalTexture;

            /// <summary>保留的 SMBE 原图相对路径。</summary>
            public string smbeTexture;

            /// <summary>派生 Specular/Gloss 贴图相对路径。</summary>
            public string specGlossTexture;

            /// <summary>派生 Emission 贴图相对路径。</summary>
            public string emissionTexture;

            /// <summary>是否启用 Alpha Clipping。</summary>
            public bool alphaClip;

            /// <summary>是否需要人工复核透明模式。</summary>
            public bool reviewTransparency;

            /// <summary>Alpha Clipping 阈值。</summary>
            public float cutoff;

            /// <summary>源节点组中的发光强度。</summary>
            public float emissionStrength;
        }

        /// <summary>
        /// 一张输出贴图及其所有材质用途。
        /// </summary>
        [Serializable]
        private sealed class TextureRecord
        {
            /// <summary>Blender 图片数据块或派生来源名称。</summary>
            public string sourceName;

            /// <summary>相对于包根目录的贴图路径。</summary>
            public string path;

            /// <summary>base、normal、smbe、specGloss 或 emission 用途。</summary>
            public string[] kinds = Array.Empty<string>();

            /// <summary>输出文件内容哈希。</summary>
            public string sha1;
        }

        #endregion
    }
}
#endif
