using System;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.SkillSystem
{
    /// <summary>保存由 GAS AbilityTask 播放的 SkillConfig 作者配置。</summary>
    [Serializable]
    public sealed class PlaySkillConfigGameplayAbilityTaskConfig : GameplayAbilityTaskConfig
    {
        [SerializeField, Tooltip("当前 Task 启动时交给角色 SkillRuntimeHost 播放的时间轴。")]
        private SkillConfig skillConfig;

        /// <summary>获取本 Task 使用的 SkillConfig。</summary>
        public SkillConfig SkillConfig => skillConfig;

        /// <summary>创建空配置，供 SerializeReference 与编辑器类型选择器使用。</summary>
        public PlaySkillConfigGameplayAbilityTaskConfig()
        {
        }

        /// <summary>创建引用指定 SkillConfig 的配置。</summary>
        /// <param name="config">需要播放的时间轴资产。</param>
        public PlaySkillConfigGameplayAbilityTaskConfig(SkillConfig config)
        {
            skillConfig = config;
        }

        internal override bool IsConfigurationValid => skillConfig != null;

        /// <summary>为本次异步 Ability 激活创建独立播放 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Runtime。</param>
        /// <returns>绑定 Runtime 与 SkillConfig 的新 Task。</returns>
        protected override GameplayAbilityTask CreateTask(AsynchronousGameplayAbilityRuntime runtime) =>
            new PlaySkillConfigGameplayAbilityTask(runtime, skillConfig);
    }
}
