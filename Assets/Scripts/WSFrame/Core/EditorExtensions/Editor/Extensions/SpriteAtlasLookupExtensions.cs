#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace WS_Modules.EditorExtensions
{
    /// <summary>表示 Sprite Atlas 反查的最终状态。</summary>
    public enum SpriteAtlasLookupStatus
    {
        /// <summary>找到唯一的默认 Sprite Atlas。</summary>
        Found,

        /// <summary>没有任何 Sprite Atlas 收录该 Sprite。</summary>
        NotFound,

        /// <summary>多个 Sprite Atlas 同时收录该 Sprite。</summary>
        Ambiguous
    }

    /// <summary>封装一次 Sprite 到 Sprite Atlas 的懒查询结果。</summary>
    public readonly struct SpriteAtlasLookupResult
    {
        /// <summary>创建一次 Atlas 反查结果。</summary>
        /// <param name="status">反查状态。</param>
        /// <param name="atlas">唯一命中的 Atlas；没有唯一命中时为空。</param>
        /// <param name="spriteName">写入运行时加载字段的 Sprite 名称。</param>
        /// <param name="matchingAtlasPaths">命中的 Atlas 资源路径，按路径排序。</param>
        public SpriteAtlasLookupResult(
            SpriteAtlasLookupStatus status,
            SpriteAtlas atlas,
            string spriteName,
            IReadOnlyList<string> matchingAtlasPaths)
        {
            Status = status;
            Atlas = atlas;
            SpriteName = spriteName ?? string.Empty;
            MatchingAtlasPaths = matchingAtlasPaths ?? Array.Empty<string>();
        }

        /// <summary>获取反查状态。</summary>
        public SpriteAtlasLookupStatus Status { get; }

        /// <summary>获取唯一命中的 Atlas。</summary>
        public SpriteAtlas Atlas { get; }

        /// <summary>获取源 Sprite 名称。</summary>
        public string SpriteName { get; }

        /// <summary>获取所有命中的 Atlas 路径。</summary>
        public IReadOnlyList<string> MatchingAtlasPaths { get; }

        /// <summary>获取是否已经找到唯一可用 Atlas。</summary>
        public bool IsFound => Status == SpriteAtlasLookupStatus.Found && Atlas != null;
    }

    /// <summary>
    /// 提供 Sprite 到 Sprite Atlas 的 Editor-only 反查扩展。
    /// 查询只在第一次遇到某个 Sprite 时扫描 Atlas，并在项目资产变化后清空缓存。
    /// </summary>
    [InitializeOnLoad]
    public static class SpriteAtlasLookupExtensions
    {
        #region 缓存与初始化

        private static readonly Dictionary<string, SpriteAtlasLookupResult> lookupCache =
            new Dictionary<string, SpriteAtlasLookupResult>(StringComparer.Ordinal);

        /// <summary>注册项目资产变化时的缓存失效回调。</summary>
        static SpriteAtlasLookupExtensions()
        {
            // projectChanged 只负责失效引用，不主动扫描，避免打开项目或刷新资产时产生全量查询。
            EditorApplication.projectChanged += InvalidateCache;
        }

        #endregion

        #region 查询

        /// <summary>反查 Sprite 唯一所属的默认 Sprite Atlas。</summary>
        /// <param name="sprite">需要反查的源 Sprite。</param>
        /// <returns>包含状态、Atlas 和 Sprite 名称的查询结果。</returns>
        /// <exception cref="ArgumentNullException">Sprite 为空时抛出。</exception>
        public static SpriteAtlasLookupResult FindSpriteAtlasReference(this Sprite sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));

            string cacheKey = CreateCacheKey(sprite);
            if (!string.IsNullOrEmpty(cacheKey) && lookupCache.TryGetValue(cacheKey, out SpriteAtlasLookupResult cached))
            {
                if (IsCachedResultValid(cached)) return cached;
                lookupCache.Remove(cacheKey);
            }

            SpriteAtlasLookupResult result = ScanAtlases(sprite);
            if (!string.IsNullOrEmpty(cacheKey) && result.IsFound) lookupCache[cacheKey] = result;
            return result;
        }

        /// <summary>清空当前 Editor 域的 Sprite Atlas 查询缓存。</summary>
        public static void InvalidateCache()
        {
            lookupCache.Clear();
        }

        /// <summary>扫描所有默认 Atlas，并确定源 Sprite 是否被唯一收录。</summary>
        /// <param name="sprite">需要匹配的源 Sprite。</param>
        /// <returns>扫描得到的 Atlas 反查结果。</returns>
        private static SpriteAtlasLookupResult ScanAtlases(Sprite sprite)
        {
            string spriteAssetPath = NormalizePath(AssetDatabase.GetAssetPath(sprite));
            if (string.IsNullOrEmpty(spriteAssetPath))
                return new SpriteAtlasLookupResult(SpriteAtlasLookupStatus.NotFound, null, sprite.name, Array.Empty<string>());

            var matches = new List<SpriteAtlas>();
            string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int index = 0; index < atlasGuids.Length; index++)
            {
                string atlasPath = AssetDatabase.GUIDToAssetPath(atlasGuids[index]);
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (atlas == null || atlas.isVariant || !ContainsSpriteAsset(atlas, spriteAssetPath)) continue;
                matches.Add(atlas);
            }

            List<string> matchingPaths = matches
                .Select(atlas => NormalizePath(AssetDatabase.GetAssetPath(atlas)))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matchingPaths.Count == 0)
                return new SpriteAtlasLookupResult(SpriteAtlasLookupStatus.NotFound, null, sprite.name, matchingPaths);
            if (matchingPaths.Count > 1)
                return new SpriteAtlasLookupResult(SpriteAtlasLookupStatus.Ambiguous, null, sprite.name, matchingPaths);

            SpriteAtlas matchingAtlas = matches.First(atlas =>
                string.Equals(NormalizePath(AssetDatabase.GetAssetPath(atlas)), matchingPaths[0], StringComparison.OrdinalIgnoreCase));
            return new SpriteAtlasLookupResult(SpriteAtlasLookupStatus.Found, matchingAtlas, sprite.name, matchingPaths);
        }

        /// <summary>判断 Atlas 的文件或文件夹 packable 是否覆盖指定 Sprite 资源。</summary>
        /// <param name="atlas">待检查的 Atlas。</param>
        /// <param name="spriteAssetPath">源 Sprite 的资源路径。</param>
        /// <returns>packable 覆盖该资源时返回 true。</returns>
        private static bool ContainsSpriteAsset(SpriteAtlas atlas, string spriteAssetPath)
        {
            UnityEngine.Object[] packables = SpriteAtlasExtensions.GetPackables(atlas);
            for (int index = 0; index < packables.Length; index++)
            {
                UnityEngine.Object packable = packables[index];
                if (packable == null) continue;
                string packablePath = NormalizePath(AssetDatabase.GetAssetPath(packable));
                if (string.IsNullOrEmpty(packablePath)) continue;
                if (AssetDatabase.IsValidFolder(packablePath))
                {
                    string folderPrefix = packablePath.TrimEnd('/') + "/";
                    if (spriteAssetPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(packablePath, spriteAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>创建稳定的 Sprite 子资源缓存键。</summary>
        /// <param name="sprite">源 Sprite。</param>
        /// <returns>由 GUID 和 local file ID 组成的键；无法解析时返回空字符串。</returns>
        private static string CreateCacheKey(Sprite sprite)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId) ||
                string.IsNullOrEmpty(guid)) return string.Empty;
            return $"{guid}:{localId}";
        }

        /// <summary>确认缓存中的 Atlas 仍然是有效资产。</summary>
        /// <param name="result">缓存结果。</param>
        /// <returns>缓存仍可使用时返回 true。</returns>
        private static bool IsCachedResultValid(SpriteAtlasLookupResult result)
        {
            return result.IsFound &&
                   result.Atlas != null &&
                   !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(result.Atlas));
        }

        /// <summary>统一 Unity 资源路径分隔符。</summary>
        /// <param name="path">原始路径。</param>
        /// <returns>使用正斜杠的路径。</returns>
        private static string NormalizePath(string path) => path?.Replace('\\', '/') ?? string.Empty;

        #endregion
    }
}
#endif
