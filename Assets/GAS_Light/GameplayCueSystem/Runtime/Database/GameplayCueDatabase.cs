using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>保存 Cue 作者资产列表，并在运行时构建 CueTag 到 CueData 的索引。</summary>
    [CreateAssetMenu(fileName = "GameplayCueDatabase", menuName = "WSFrame/GAS/Gameplay Cue Database")]
    public sealed class GameplayCueDatabase : ScriptableObject
    {
        #region 作者数据与运行时索引
        [SerializeField]
        private List<GameplayCueData> cues = new();
        private readonly Dictionary<GameplayTag, GameplayCueData> cueIndex = new();
        #endregion

        #region 属性
        /// <summary>获取作者配置的 Cue 资产列表。</summary>
        public IReadOnlyList<GameplayCueData> Cues => cues;
        /// <summary>获取当前已构建的运行时 Cue 索引数量。</summary>
        public int Count => cueIndex.Count;
        #endregion

        #region 运行时操作
        /// <summary>重建运行时索引；重复标签不会覆盖先登记的 Cue。</summary>
        /// <returns>索引中成功登记的 Cue 数量。</returns>
        public int BuildRuntimeIndex()
        {
            cueIndex.Clear();
            if (cues == null) return 0;

            int registeredCount = 0;
            for (int i = 0; i < cues.Count; i++)
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
                if (cueIndex.ContainsKey(cue.CueTag))
                {
                    Debug.LogError($"GameplayCueDatabase '{name}' 中存在重复 CueTag：{cue.CueTag}，保留首次登记项。", cue);
                    continue;
                }

                cueIndex.Add(cue.CueTag, cue);
                registeredCount++;
            }

            return registeredCount;
        }

        /// <summary>尝试按稳定 CueTag 获取 CueData。</summary>
        /// <param name="cueTag">待查找的 CueTag。</param>
        /// <param name="cue">找到的 CueData。</param>
        /// <returns>找到有效 CueData 时返回 true。</returns>
        public bool TryGetCue(GameplayTag cueTag, out GameplayCueData cue) =>
            cueIndex.TryGetValue(cueTag, out cue);
        #endregion

#if UNITY_EDITOR
        // 编辑器修改列表时立即提示空项、非法 Tag 和重复映射，运行时仍以首次登记项为准。
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
        }
#endif
    }
}
