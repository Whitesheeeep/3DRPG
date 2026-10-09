#if UNITY_EDITOR
using System;
using RPG.Game;
using RPG.NPC;
using RPG.SaveSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule.Test
{
    /// <summary>在 Editor Play Mode 中验证 Toggle 完成状态与存档快照往返。</summary>
    public sealed class DialogueInteractionOdinTester : MonoBehaviour
    {
        #region Inspector 操作

        /// <summary>通过独立测试身份执行完成、快照恢复和状态清理验证。</summary>
        [Button("验证 Toggle 完成与存档往返")]
        [InfoBox("请在 GameArchitecture 已启动的 Play Mode 中运行。测试使用临时 NPCId 和 OptionId，结束后会恢复原有完成记录。")]
        private void TestToggleSaveRoundTrip()
        {
            DialogueInteractionManager manager =
                GameArchitecture.Interface.GetManager<DialogueInteractionManager>();
            ISaveModule saveModule = new DialogueInteractionSaveModule(manager);
            var originalSnapshot = (DialogueInteractionSaveSnapshot)saveModule.CaptureSnapshot();
            // 拒绝在已有 Toggle 会话运行时执行快照覆盖测试，避免改变玩家当前流程。
            saveModule.ValidateSnapshot(originalSnapshot);
            DialogueSession testSession = null;
            bool managerStateChanged = false;

            try
            {
                NPCId testNpcId = new NPCId($"dialogue-test-{Guid.NewGuid():N}");
                string testOptionId = Guid.NewGuid().ToString("N");
                var request = new DialogueRequest(null, null, null);
                testSession = new DialogueSession(request);
                manager.TrackToggleSession(testNpcId, testOptionId, testSession);
                managerStateChanged = true;
                testSession.End("Odin 快照往返测试", DialogueEndStatus.Completed);

                if (!manager.IsCompleted(testNpcId, testOptionId))
                    throw new InvalidOperationException("正常结束的测试会话没有登记 Toggle 完成状态。");

                ISaveModuleSnapshot completedSnapshot = saveModule.CaptureSnapshot();
                saveModule.ValidateSnapshot(completedSnapshot);
                saveModule.RestoreSnapshot(completedSnapshot);
                if (!manager.IsCompleted(testNpcId, testOptionId))
                    throw new InvalidOperationException("恢复完成快照后未保留 Toggle 状态。");

                ISaveModuleSnapshot emptySnapshot = saveModule.CreateDefaultSnapshot();
                saveModule.ValidateSnapshot(emptySnapshot);
                saveModule.RestoreSnapshot(emptySnapshot);
                if (manager.IsCompleted(testNpcId, testOptionId))
                    throw new InvalidOperationException("恢复空快照后仍残留 Toggle 完成状态。");

                Debug.Log("[DialogueInteractionOdinTester] Toggle 完成、快照恢复和空快照替换验证通过。", this);
            }
            finally
            {
                if (managerStateChanged)
                {
                    if (testSession != null && !testSession.IsEnded)
                        testSession.End("Odin 测试清理", DialogueEndStatus.Failed);

                    saveModule.RestoreSnapshot(originalSnapshot);
                }
            }
        }

        #endregion
    }
}
#endif
