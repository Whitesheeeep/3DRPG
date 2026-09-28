using System.Collections.Generic;
using System;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>保存 CueData 与按具体数据类型注册的共享 Handler，并构建精确 Tag 索引。</summary>
    [CreateAssetMenu(fileName = "GameplayCueDatabase", menuName = "WSFrame/GAS/Gameplay Cue Database")]
    public sealed class GameplayCueDatabase : ScriptableObject
    {
        #region 作者数据与运行时索引

        // 作者配置
        [SerializeField]
        private List<GameplayCueData> cues = new();
        [SerializeField, Tooltip("共享 Handler 按 CueData 的具体类型登记；每种类型只能指定一个 Handler。")]
        private List<GameplayCueHandlerSO> handlers = new();

        // 运行时索引
        // key：CueTag；value：该精确标签对应的唯一 CueData。
        private readonly Dictionary<GameplayTag, GameplayCueData> cueByTagMap = new();
        // key：CueData 具体 Type；value：该类型对应的唯一共享 Handler SO。
        private readonly Dictionary<Type, GameplayCueHandlerSO> handlerByDataTypeMap = new();
        #endregion

        #region 属性
        /// <summary>获取作者配置的 Cue 资产列表。</summary>
        public IReadOnlyList<GameplayCueData> Cues => cues;
        /// <summary>获取作者配置的共享 Handler SO 列表。</summary>
        public IReadOnlyList<GameplayCueHandlerSO> Handlers => handlers;
        /// <summary>获取当前已构建的运行时 Cue 索引数量。</summary>
        public int Count => cueByTagMap.Count;
        #endregion

        #region 运行时操作
        /// <summary>重建运行时索引；重复标签不会覆盖先登记的 Cue。</summary>
        /// <returns>索引中成功登记的 Cue 数量。</returns>
        public int BuildRuntimeIndex()
        {
            cueByTagMap.Clear();
            handlerByDataTypeMap.Clear();
            int registeredCount = 0;
            for (int i = 0; cues != null && i < cues.Count; i++)
            {
                GameplayCueData cue = cues[i];
                if (cue == null)
                {
                    Debug.LogError($"GameplayCueDatabase '{name}' 的 Cues[{i}] 为空。", this);
                    continue;
                }
                if (!cue.CueTag.IsValid)
                {
                    Debug.LogError($"GameplayCueDatabase '{name}' 的 CueData '{cue.name}' 使用了非法 CueTag。", cue);
                    continue;
                }
                if (cueByTagMap.ContainsKey(cue.CueTag))
                {
                    Debug.LogError($"GameplayCueDatabase '{name}' 中存在重复 CueTag：{cue.CueTag}，保留首次登记项。", cue);
                    continue;
                }

                cueByTagMap.Add(cue.CueTag, cue);
                registeredCount++;
            }

            if (handlers != null)
            {
                for (int index = 0; index < handlers.Count; index++)
                {
                    GameplayCueHandlerSO handler = handlers[index];
                    if (handler == null)
                    {
                        Debug.LogError($"GameplayCueDatabase '{name}' 的 Handlers[{index}] 为空。", this);
                        continue;
                    }

                    Type dataType = handler.CueDataType;
                    if (dataType == null || !typeof(GameplayCueData).IsAssignableFrom(dataType) || dataType.IsAbstract)
                    {
                        Debug.LogError($"GameplayCueHandlerSO '{handler.name}' 声明了无效 CueData 类型。", handler);
                        continue;
                    }
                    if (handlerByDataTypeMap.ContainsKey(dataType))
                    {
                        Debug.LogError(
                            $"GameplayCueDatabase '{name}' 为 CueData 类型 '{dataType.Name}' 重复登记共享 Handler；保留首次登记项。",
                            handler);
                        continue;
                    }

                    handlerByDataTypeMap.Add(dataType, handler);
                }
            }

            Debug.Log($"[GameplayCueDatabase] '{name}' 已构建索引，Cue={registeredCount}，Handler={handlerByDataTypeMap.Count}。", this);
            return registeredCount;
        }

        /// <summary>尝试按稳定 CueTag 获取 CueData。</summary>
        /// <param name="cueTag">待查找的 CueTag。</param>
        /// <param name="cue">找到的 CueData。</param>
        /// <returns>找到有效 CueData 时返回 true。</returns>
        public bool TryGetCue(GameplayTag cueTag, out GameplayCueData cue) =>
            cueByTagMap.TryGetValue(cueTag, out cue);

        /// <summary>尝试按 CueData 的具体类型获取唯一共享 Handler。</summary>
        /// <param name="dataType">CueData 的运行时具体类型。</param>
        /// <param name="handler">找到的共享 Handler SO。</param>
        /// <returns>该类型已经登记共享 Handler 时返回 true。</returns>
        public bool TryGetHandler(Type dataType, out GameplayCueHandlerSO handler) =>
            handlerByDataTypeMap.TryGetValue(dataType, out handler);
        #endregion

#if UNITY_EDITOR
        /// <summary>编辑器修改配置后提示空项、非法 Tag 和重复映射。</summary>
        private void OnValidate()
        {
            if (cues == null) return;
            var unique = new HashSet<GameplayTag>();
            for (int i = 0; i < cues.Count; i++)
            {
                GameplayCueData cue = cues[i];
                if (cue == null)
                {
                    Debug.LogError($"GameplayCueDatabase '{name}' 的 Cues[{i}] 为空。", this);
                    continue;
                }
                if (!cue.CueTag.IsValid)
                    Debug.LogError($"GameplayCueDatabase '{name}' 的 CueData '{cue.name}' 使用了非法 CueTag。", cue);
                else if (!unique.Add(cue.CueTag))
                    Debug.LogError($"GameplayCueDatabase '{name}' 中存在重复 CueTag：{cue.CueTag}。", cue);
            }

            if (handlers == null) return;
            var registeredHandlerTypeSet = new HashSet<Type>();
            for (int index = 0; index < handlers.Count; index++)
            {
                GameplayCueHandlerSO handler = handlers[index];
                if (handler == null) continue;
                Type dataType = handler.CueDataType;
                if (dataType == null || !typeof(GameplayCueData).IsAssignableFrom(dataType) || dataType.IsAbstract)
                    Debug.LogError($"GameplayCueHandlerSO '{handler.name}' 声明了无效 CueData 类型。", handler);
                else if (!registeredHandlerTypeSet.Add(dataType))
                    Debug.LogError($"GameplayCueDatabase '{name}' 为 '{dataType.Name}' 重复登记共享 Handler。", handler);
            }
        }
#endif
    }
}
