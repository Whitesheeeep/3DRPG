#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 校验动画素材并生成不依赖拖拽生命周期的批量创建请求。
    /// </summary>
    internal sealed class AnimationDropHandler : ITrackDropHandler
    {
        /// <summary>
        /// 判断整批素材是否都是 Project 中持久化的 AnimationClip。
        /// </summary>
        public bool CanAccept(IReadOnlyList<UnityEngine.Object> assets)
        {
            if (assets == null || assets.Count == 0) return false;
            for (int index = 0; index < assets.Count; index++)
                if (assets[index] is not AnimationClip clip || !EditorUtility.IsPersistent(clip))
                    return false;
            return true;
        }

        /// <summary>
        /// 复制动画素材引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public IItemCreateRequest CreateRequest(IReadOnlyList<UnityEngine.Object> assets, int startFrame)
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
    internal sealed class VfxDropHandler : ITrackDropHandler
    {
        private readonly EditorConfig config;

        /// <summary>
        /// 创建特效拖入处理器，并保存只读的编辑器窗口配置引用。
        /// </summary>
        public VfxDropHandler(EditorConfig config) =>
            this.config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// 判断整批素材是否都是 Project 中的 Prefab Asset。
        /// </summary>
        public bool CanAccept(IReadOnlyList<UnityEngine.Object> assets)
        {
            if (assets == null || assets.Count == 0) return false;
            for (int index = 0; index < assets.Count; index++)
            {
                if (assets[index] is not GameObject prefab ||
                    !EditorUtility.IsPersistent(prefab) ||
                    !PrefabUtility.IsPartOfPrefabAsset(prefab))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 复制 Prefab 引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public IItemCreateRequest CreateRequest(IReadOnlyList<UnityEngine.Object> assets, int startFrame)
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
    internal sealed class AudioDropHandler : ITrackDropHandler
    {
        /// <summary>
        /// 判断整批素材是否都是 Project 中持久化的 AudioClip。
        /// </summary>
        public bool CanAccept(IReadOnlyList<UnityEngine.Object> assets)
        {
            if (assets == null || assets.Count == 0) return false;
            for (int index = 0; index < assets.Count; index++)
                if (assets[index] is not AudioClip clip || !EditorUtility.IsPersistent(clip))
                    return false;
            return true;
        }

        /// <summary>
        /// 复制 AudioClip 引用并生成落在指定整数帧的创建请求。
        /// </summary>
        public IItemCreateRequest CreateRequest(IReadOnlyList<UnityEngine.Object> assets, int startFrame)
        {
            if (!CanAccept(assets)) throw new ArgumentException("音频素材集合无效。", nameof(assets));
            AudioClip[] clips = new AudioClip[assets.Count];
            for (int index = 0; index < assets.Count; index++) clips[index] = (AudioClip)assets[index];
            return new AudioCreateRequest(clips, startFrame);
        }
    }
}
#endif