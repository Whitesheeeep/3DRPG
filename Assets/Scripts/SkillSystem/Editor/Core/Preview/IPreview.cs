#if UNITY_EDITOR
using System;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 定义时间轴播放控制器与后续场景预览实现之间的最小采样接口。
    /// </summary>
    internal interface IPreview : IDisposable
    {
        /// <summary>
        /// 切换播放控制器使用的配置并复位播放头。
        /// </summary>
        void SetSkillConfig(SkillConfig config);
        /// <summary>
        /// 保存固定演示角色的 GlobalObjectId。
        /// </summary>
        void SetPreviewActor(GameObject actor);
        /// <summary>
        /// 采样指定整数帧的预览状态。
        /// </summary>
        void SampleFrame(int frame);
        /// <summary>
        /// 停止播放并将播放头复位到第 0 帧。
        /// </summary>
        void Stop();
        /// <summary>
        /// 清理预览实现持有的全部状态。
        /// </summary>
        void Clear();
    }
}
#endif
