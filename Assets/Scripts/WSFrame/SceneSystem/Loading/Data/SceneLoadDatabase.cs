using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.LogModule;

namespace WS_Modules.SceneModule
{
    /// <summary>集中保存项目可加载场景配置，避免运行时代码扫描项目资产。</summary>
    [CreateAssetMenu(fileName = "SceneLoadDatabase", menuName = "WSFrame/Scene Loading/Database")]
    public sealed class SceneLoadDatabase : ScriptableObject
    {
        #region 场景配置列表

        // 配置：Inspector 列表保留场景配置的编辑顺序；运行时查询使用下方一次构建的索引。
        [SerializeField]
        private List<SceneLoadConfig> sceneConfigs = new();

        // key：SceneId；value：唯一对应的场景配置。索引仅在数据库注册时构建。
        [NonSerialized]
        private Dictionary<string, SceneLoadConfig> sceneConfigByIdMap;

        #endregion

        #region 查询

        /// <summary>获取数据库内按配置顺序排列的只读场景集合。</summary>
        public IReadOnlyList<SceneLoadConfig> SceneConfigs => sceneConfigs;

        /// <summary>按稳定 ID 从注册时构建的索引中查询场景配置。</summary>
        /// <param name="sceneId">稳定场景 ID。</param>
        /// <returns>匹配的场景配置。</returns>
        /// <exception cref="ArgumentException">SceneId 为空或空白时抛出。</exception>
        /// <exception cref="InvalidOperationException">数据库尚未建立运行时索引时抛出。</exception>
        /// <exception cref="KeyNotFoundException">数据库中不存在该 SceneId 时抛出。</exception>
        public SceneLoadConfig GetConfig(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new ArgumentException("[SceneLoadDatabase] SceneId 不能为空。", nameof(sceneId));

            if (sceneConfigByIdMap == null)
                throw new InvalidOperationException("[SceneLoadDatabase] 数据库尚未建立 SceneId 索引，请先通过 SceneLoadingSystem.RegisterDatabase 注册。");

            return sceneConfigByIdMap.TryGetValue(sceneId, out SceneLoadConfig config)
                ? config
                : throw new KeyNotFoundException($"[SceneLoadDatabase] 找不到场景配置 '{sceneId}'。");
        }

        #endregion

        #region 索引构建

        /// <summary>验证全部场景配置并一次性建立运行时 SceneId 查询索引。</summary>
        /// <exception cref="InvalidOperationException">场景列表为空引用、配置项为空、SceneId 无效或重复时抛出。</exception>
        public void ValidateAndBuildIndex()
        {
            if (sceneConfigs == null)
                throw new InvalidOperationException("[SceneLoadDatabase] 场景配置列表为空引用。");
            if (sceneConfigs.Count == 0)
                throw new InvalidOperationException("[SceneLoadDatabase] 至少需要配置一个可加载场景。");

            var validatedSceneConfigByIdMap = new Dictionary<string, SceneLoadConfig>(sceneConfigs.Count, StringComparer.Ordinal);
            for (int index = 0; index < sceneConfigs.Count; index++)
            {
                SceneLoadConfig config = sceneConfigs[index];
                if (config == null)
                    throw new InvalidOperationException($"[SceneLoadDatabase] 第 {index} 个场景配置为空。");
                if (string.IsNullOrWhiteSpace(config.SceneId))
                    throw new InvalidOperationException($"[SceneLoadDatabase] 场景配置 '{config.name}' 的 SceneId 为空。");
                if (!validatedSceneConfigByIdMap.TryAdd(config.SceneId, config))
                    throw new InvalidOperationException($"[SceneLoadDatabase] SceneId '{config.SceneId}' 重复配置。");
            }

            // 只有整份数据库通过校验后才替换索引，避免查询到部分构建结果。
            sceneConfigByIdMap = validatedSceneConfigByIdMap;
            WSLog.Log($"[SceneLoadDatabase] 场景配置校验并建立索引完成，sceneCount={sceneConfigByIdMap.Count}。");
        }

        #endregion
    }
}
