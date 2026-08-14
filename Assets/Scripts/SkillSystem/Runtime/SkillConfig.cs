using System;
using System.Collections.Generic;
using RPG.Markers;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存技能时间轴的运行时帧参数和有序轨道子资产引用。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "RPG/Skill/Skill Config")]
    public sealed class SkillConfig : ScriptableObject
    {
        [SerializeField, ReadOnly, LabelText("技能 ID")]
        private string id = string.Empty;
        [SerializeField, ReadOnly, Min(1), LabelText("FPS")]
        private int frameRate = 30;
        [SerializeField, ReadOnly, Min(1), LabelText("总帧")]
        private int durationFrames = 1;
        [SerializeField, ReadOnly, LabelText("应用根运动")]
        private bool isRootMotion;
        [SerializeField, ReadOnly, LabelText("轨道")]
        private List<TrackConfigBase> tracks = new();

        public string Id => id;
        public int FrameRate => frameRate;
        public int DurationFrames => durationFrames;
        /// <summary>获取该时间轴播放时是否消费 Animator 根位移和根旋转。</summary>
        public bool IsRootMotion => isRootMotion;
        public IReadOnlyList<TrackConfigBase> Tracks => tracks;
    }
}
