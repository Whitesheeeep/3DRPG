#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/*
 * 本文件夹主要处理从 Project 中拖拽素材到技能编辑器窗口进行落位请求处理
 */

namespace RPG.SkillSystem.Editor
{
    internal abstract class TrackDropHandlerBase<T> : ITrackDropHandler
        where T : Object
    {
        /// <summary>
        /// 判断整批 Project 素材是否可以被该轨道接收。
        /// </summary>
        /// <typeparam name="T">素材类型。</typeparam>
        /// <param name="assets">要检查的素材列表。</param>
        /// <returns>如果可以接收则返回 true，否则返回 false。</returns>
        public bool CanAccept(IReadOnlyList<Object> assets)
        {
            if (assets == null || assets.Count == 0) return false;
            // 判断素材是否是对应的类型且是 Project 中持久化的
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] is not T clip || !EditorUtility.IsPersistent(clip))
                    return false;
            }

            return true;
        }

        public abstract IItemCreateRequest CreateRequest(IReadOnlyList<Object> assets, int startFrame);
    }

    /// <summary>
    /// 校验动画素材并生成不依赖拖拽生命周期的批量创建请求。
    /// </summary>
    internal sealed class AnimationDropHandler : TrackDropHandlerBase<AnimationClip>
    {
        /// <summary>
        /// 复制动画素材引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public override IItemCreateRequest CreateRequest(IReadOnlyList<Object> assets, int startFrame)
        {
            if (!CanAccept(assets)) throw new ArgumentException("动画素材集合无效。", nameof(assets));
            AnimationClip[] clips = new AnimationClip[assets.Count];
            for (int index = 0; index < assets.Count; index++) clips[index] = (AnimationClip)assets[index];
            return new AnimationCreateRequest(clips, startFrame);
        }
    }

    /// <summary>
    /// 校验特效 Prefab 并生成使用编辑器默认持续帧的批量创建请求。
    /// </summary>
    internal sealed class VfxDropHandler : TrackDropHandlerBase<GameObject>
    {
        private readonly EditorConfig config;

        /// <summary>
        /// 创建特效拖入处理器，并保存只读的编辑器窗口配置引用。
        /// </summary>
        public VfxDropHandler(EditorConfig config) =>
            this.config = config ?? throw new ArgumentNullException(nameof(config));


        /// <summary>
        /// 复制 Prefab 引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public override IItemCreateRequest CreateRequest(IReadOnlyList<Object> assets, int startFrame)
        {
            if (!CanAccept(assets)) throw new ArgumentException("特效素材集合无效。", nameof(assets));
            GameObject[] prefabs = new GameObject[assets.Count];
            for (int index = 0; index < assets.Count; index++) prefabs[index] = (GameObject)assets[index];
            return new VfxCreateRequest(prefabs, startFrame, config.DefaultVfxClipDurationFrames);
        }
    }

    /// <summary>
    /// 校验音频素材并生成使用默认音量与 Pitch 的批量创建请求。
    /// </summary>
    internal sealed class AudioDropHandler : TrackDropHandlerBase<AudioClip>
    {
        /// <summary>
        /// 复制 AudioClip 引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public override IItemCreateRequest CreateRequest(IReadOnlyList<Object> assets, int startFrame)
        {
            if (!CanAccept(assets)) throw new ArgumentException("音频素材集合无效。", nameof(assets));
            AudioClip[] clips = new AudioClip[assets.Count];
            for (int index = 0; index < assets.Count; index++) clips[index] = (AudioClip)assets[index];
            return new AudioCreateRequest(clips, startFrame);
        }
    }
}
#endif