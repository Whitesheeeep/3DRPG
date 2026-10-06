using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.SceneModule
{
    /// <summary>集中保存项目可加载场景配置，避免运行时代码扫描项目资产。</summary>
    [CreateAssetMenu(fileName = "SceneLoadDatabase", menuName = "WSFrame/Scene Loading/Database")]
    public sealed class SceneLoadDatabase : ScriptableObject
    {
        #region 场景配置列表

        // 配置：稳定 SceneId 唯一定位一个场景；查询边界检测重复项。
        // 一个稳定 SceneId 对应一份 SceneLoadConfig；重复 ID 在查询边界立即报错。
        [SerializeField]
        private List<SceneLoadConfig> sceneConfigs = new();

        #endregion

        #region 查询

        /// <summary>获取数据库内按配置顺序排列的只读场景集合。</summary>
        public IReadOnlyList<SceneLoadConfig> SceneConfigs => sceneConfigs;

        /// <summary>按稳定 ID 查询场景配置，并检查空值与重复标识。</summary>
        /// <param name="sceneId">稳定场景 ID。</param>
        /// <returns>匹配的场景配置。</returns>
        public SceneLoadConfig GetConfig(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new ArgumentException("[SceneLoadDatabase] SceneId 不能为空。", nameof(sceneId));

            SceneLoadConfig result = null;
            for (int index = 0; index < sceneConfigs.Count; index++)
            {
                SceneLoadConfig config = sceneConfigs[index];
                if (config == null || !string.Equals(config.SceneId, sceneId, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException($"[SceneLoadDatabase] SceneId '{sceneId}' 重复配置。");
                result = config;
            }

            return result ?? throw new KeyNotFoundException($"[SceneLoadDatabase] 找不到场景配置 '{sceneId}'。");
        }

        #endregion
    }
}
