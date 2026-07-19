using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace WS_Modules.Json
{
    public enum JsonType
    {
        Newtonsoft,
        UnityJson
    }

    /// <summary>
    /// Centralized JSON helper supporting both Newtonsoft and Unity JsonUtility backends.
    /// </summary>
    public static class JsonMgr
    {
        private static JsonType _defaultJsonType = JsonType.Newtonsoft;
        private static JsonSerializerSettings _serializerSettings;
        /// <summary>
        /// 路径解析器，允许用户自定义相对路径到绝对路径的转换逻辑
        /// 默认实现将相对路径解析到 Application.persistentDataPath 下，并自动添加 .json 扩展名（如果没有）
        /// 并且会确保路径分隔符统一为 '/' 以及去除路径开头的斜杠，以避免路径解析错误
        /// 在默认拼接 Application.persistentDataPath 后可选的路径后处理器，传 null 表示直接使用默认路径。
        /// </summary>
        private static Func<string, string> _pathResolver;

        static JsonMgr()
        {
            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
            _pathResolver = ResolveDefaultPath;
        }

        public static void SetDefaultType(JsonType jsonType) => _defaultJsonType = jsonType;

        public static void SetSerializerSettings(JsonSerializerSettings settings)
        {
            _serializerSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public static void SetPathResolver(Func<string, string> resolver)
        {
            _pathResolver = resolver ?? ResolveDefaultPath;
        }

        public static string Serialize<T>(T data, bool prettyPrint = false, JsonType? overrideType = null)
        {
            var type = overrideType ?? _defaultJsonType;
            try
            {
                if (type == JsonType.UnityJson)
                {
                    return JsonUtility.ToJson(data, prettyPrint);
                }

                var formatting = prettyPrint ? Formatting.Indented : Formatting.None;
                return JsonConvert.SerializeObject(data, formatting, _serializerSettings);
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonMgr Serialize error ({type}): {ex}");
                return null;
            }
        }

        public static T Deserialize<T>(string json, JsonType? overrideType = null)
        {
            return TryDeserialize(json, out T data, overrideType) ? data : default;
        }

        public static bool TryDeserialize<T>(string json, out T data, JsonType? overrideType = null)
        {
            data = default;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            var type = overrideType ?? _defaultJsonType;
            try
            {
                data = type == JsonType.UnityJson
                    ? JsonUtility.FromJson<T>(json)
                    : JsonConvert.DeserializeObject<T>(json, _serializerSettings);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonMgr Deserialize error ({type}): {ex}");
                data = default;
                return false;
            }
        }

        public static bool Save<T>(T data, string relativePath, bool prettyPrint = false, JsonType? overrideType = null)
        {
            var json = Serialize(data, prettyPrint, overrideType);
            if (json == null)
            {
                return false;
            }

            var fullPath = ResolveFullPath(relativePath);
            try
            {
                EnsureDirectory(fullPath);
                File.WriteAllText(fullPath, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonMgr Save error ({fullPath}): {ex}");
                return false;
            }
        }

        public static T Load<T>(string relativePath, T fallback = default, JsonType? overrideType = null)
        {
            return TryLoad(relativePath, out T data, overrideType) ? data : fallback;
        }

        public static bool TryLoad<T>(string relativePath, out T data, JsonType? overrideType = null)
        {
            data = default;
            var fullPath = ResolveFullPath(relativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"JsonMgr Load warning: File not found at {fullPath}");
                return false;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                return TryDeserialize(json, out data, overrideType);
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonMgr Load error ({fullPath}): {ex}");
                data = default;
                return false;
            }
        }

        public static bool Delete(string relativePath)
        {
            var fullPath = ResolveFullPath(relativePath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            try
            {
                File.Delete(fullPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"JsonMgr Delete error ({fullPath}): {ex}");
                return false;
            }
        }

        public static bool Exists(string relativePath)
        {
            return File.Exists(ResolveFullPath(relativePath));
        }

        private static string ResolveFullPath(string relativePath)
        {
            var defaultPath = ResolveDefaultPath(relativePath);
            return _pathResolver != null ? _pathResolver(defaultPath) : defaultPath;
        }

        private static string ResolveDefaultPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("relativePath cannot be null or empty", nameof(relativePath));
            }

            var sanitized = relativePath.Replace("\\", "/").TrimStart('/');
            if (!Path.HasExtension(sanitized))
            {
                sanitized += ".json";
            }

            var combined = Path.Combine(Application.persistentDataPath, sanitized);
            return Path.GetFullPath(combined);
        }

        private static void EnsureDirectory(string fullPath)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}

