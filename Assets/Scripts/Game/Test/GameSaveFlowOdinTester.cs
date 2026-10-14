#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Game.Saving;
using RPG.SaveSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.Game.Tests
{
    /// <summary>通过 Odin 验证摘要读写、十档列表与当前会话保存，不改写其他档位。</summary>
    public sealed class GameSaveFlowOdinTester : MonoBehaviour
    {
        #region 测试入口
        /// <summary>验证 Manager 组合摘要在完整读取与摘要读取之间保持四槽一致。</summary>
        [Button("验证业务摘要对称读写")]
        public void TestSummaryRoundTrip() => TestSummaryAsync().Forget(Debug.LogException);

        /// <summary>读取正式十档列表，不恢复游戏状态。</summary>
        [Button("刷新并检查十档列表")]
        public void TestSlotList() => TestSlotsAsync().Forget(Debug.LogException);

        /// <summary>只保存当前选定会话，不创建或删除其他卡位。</summary>
        [Button("保存当前游戏")]
        public void SaveCurrent() => SaveCurrentAsync().Forget(Debug.LogException);
        #endregion

        #region 验证过程
        /// <summary>用同一 Codec 验证两种读法及预置集合替换，不写入正式目录。</summary>
        /// <returns>验证完成。</returns>
        private async UniTask TestSummaryAsync()
        {
            var serializer = new NewtonsoftJsonSaveSerializer();
            var codec = new SaveBusinessSummaryCodec(typeof(GameSaveSummary));
            var business = new GameSaveSummary();
            for (int index = 0; index < 4; index++)
                business.PartyMembers.Add(new GameSavePartyMemberSummary { CharacterId = "character.test." + index, CharacterName = "测试角色 " + index });
            var envelope = new SaveEnvelope(new SaveSlotSummary(SaveSlotId.CreateNew(), business,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1), new System.Collections.Generic.List<SaveModuleData>
                {
                    new SaveModuleData(RPG.Character.CharacterPartySaveModule.StableModuleId, 1,
                        new RPG.Character.CharacterPartySaveSnapshot())
                });
            using (var stream = new MemoryStream())
            {
                SaveResult written = await serializer.SerializeAsync(envelope, stream, codec, CancellationToken.None);
                if (!written.IsSuccess) throw new InvalidOperationException(written.Message);
                stream.Position = 0;
                var resolver = new SaveSnapshotTypeRegistry();
                resolver.Register<RPG.Character.CharacterPartySaveSnapshot>(RPG.Character.CharacterPartySaveModule.StableModuleId, 1);
                SaveResult<SaveEnvelope> full = await serializer.DeserializeAsync(stream, resolver, codec, CancellationToken.None);
                stream.Position = 0;
                SaveResult<SaveSlotSummary> summary = await serializer.ReadSummaryAsync(stream, codec, CancellationToken.None);
                if (!full.IsSuccess || !summary.IsSuccess) throw new InvalidOperationException("[GameSaveFlowOdinTester] 摘要解析失败。");
                ((RPG.Character.CharacterPartySaveSnapshot)full.Value.Modules[0].Snapshot).ValidateShape();
                var first = (GameSaveSummary)full.Value.Summary.BusinessSummary;
                var second = (GameSaveSummary)summary.Value.BusinessSummary;
                first.ValidateShape(); second.ValidateShape();
                for (int index = 0; index < 4; index++)
                    if (first.PartyMembers[index].CharacterId != business.PartyMembers[index].CharacterId ||
                        second.PartyMembers[index].CharacterName != business.PartyMembers[index].CharacterName)
                        throw new InvalidOperationException("[GameSaveFlowOdinTester] 四槽摘要往返不一致。");
            }
            Debug.Log("[GameSaveFlowOdinTester] 摘要对称写入、完整读取与只读摘要一致，memberCount=4。", this);
        }

        /// <summary>从实际服务读取固定十卡，并核对 GUID 唯一性。</summary>
        /// <returns>验证完成。</returns>
        private async UniTask TestSlotsAsync()
        {
            var saves = GameArchitecture.Interface.GetSystem<GameSaveSystem>();
            await saves.RefreshSlotsAsync();
            var ids = new System.Collections.Generic.HashSet<SaveSlotId>();
            foreach (GameSaveSlot slot in saves.Slots) if (!ids.Add(slot.SlotId)) throw new InvalidOperationException("卡位重复。");
            if (ids.Count != 10) throw new InvalidOperationException("卡位数量不是十个。");
            Debug.Log("[GameSaveFlowOdinTester] 正式卡位目录检查通过，count=10。", this);
        }

        /// <summary>等待当前活动档保存并输出实际结果。</summary>
        /// <returns>保存完成。</returns>
        private async UniTask SaveCurrentAsync()
        {
            var result = await GameArchitecture.Interface.GetSystem<GameSaveSystem>().SaveCurrentAsync();
            Debug.Log($"[GameSaveFlowOdinTester] 当前档保存完成，success={result.IsSuccess}, message={result.Message}。", this);
        }
        #endregion
    }
}
#endif
