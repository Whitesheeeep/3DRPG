using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.DialogueSystemModule;
using RPG.SaveSystem;
using RPG.TaskSystemNS;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules.BusinessArchitecture;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Saving
{
    /// <summary>编排固定卡位、新建读档、活动会话及统一保存，不持有窗口或 Unity 资源。</summary>
    public sealed class GameSaveSystem : AbstractSystem
    {
        #region 常量与依赖字段
        /// <summary>返回主界面使用的稳定场景 ID。</summary>
        public const string TitleSceneId = "scene.start";
        private SaveManager saveManager;
        private CharacterPartyManager partyManager;
        private SaveSlotCatalogStorage catalogStorage;
        private IReadOnlyList<SaveSlotId> slotIds;
        private IReadOnlyList<GameSaveSlot> slots = Array.Empty<GameSaveSlot>();
        private SaveSlotId activeSlotId;
        private SaveSlotId pendingSlotId;
        #endregion

        #region 状态与事件
        /// <summary>卡位、保存结果或会话状态变化后通知展示层。</summary>
        public event Action Changed;
        /// <summary>旧会话清理前解除窗口流程连接。</summary>
        public event Action SessionEnding;
        /// <summary>旧会话清理完成后重建窗口流程连接。</summary>
        public event Action SessionEnded;
        /// <summary>当前会话阶段。</summary>
        public E_GameSaveSessionState State { get; private set; } = E_GameSaveSessionState.Title;
        /// <summary>是否正在实际保存。</summary>
        public bool IsSaving { get; private set; }
        /// <summary>最近一次保存失败是否仍需展示。</summary>
        public bool HasSaveFailure { get; private set; }
        /// <summary>当前档最近成功保存或加载的 UTC 时间。</summary>
        public long LastSavedAtUtcMilliseconds { get; private set; }
        /// <summary>可直接展示的操作结果。</summary>
        public string StatusMessage { get; private set; } = string.Empty;
        /// <summary>十个固定卡位的当前详情。</summary>
        public IReadOnlyList<GameSaveSlot> Slots => slots;
        /// <summary>当前活动或正在进入的档位，零表示调试会话没有选档。</summary>
        public int CurrentSlotNumber => FindSlotNumber(activeSlotId.IsValid ? activeSlotId : pendingSlotId);
        #endregion

        #region 生命周期
        /// <summary>在其他业务 System 初始化完毕后采集新游戏初始状态。</summary>
        protected override void OnInit()
        {
            saveManager = this.GetManager<SaveManager>();
            partyManager = this.GetManager<CharacterPartyManager>();
            catalogStorage = new SaveSlotCatalogStorage(Path.Combine(Application.persistentDataPath, SaveStorageDefaults.LocalDirectoryName));
            saveManager.CaptureInitialState();
            WSLog.Log("[GameSaveSystem] 已初始化会话与十档目录服务。");
        }

        /// <summary>释放业务事件，不在退出回调中启动不能等待的自动保存。</summary>
        protected override void OnDeinit()
        {
            Changed = null;
            SessionEnding = null;
            SessionEnded = null;
            WSLog.Log("[GameSaveSystem] 已释放会话订阅。");
        }
        #endregion

        #region 选档操作
        /// <summary>读取全部文件摘要并按持久卡位排列；不恢复业务数据。</summary>
        /// <returns>刷新完成的异步任务。</returns>
        public async UniTask RefreshSlotsAsync()
        {
            SaveResult<IReadOnlyList<SaveSlotDetails>> result = await saveManager.ListSlotDetailsAsync();
            if (!result.IsSuccess) throw new IOException("[GameSaveSystem] 读取存档列表失败：" + result.Message, result.Exception);
            slotIds = catalogStorage.LoadOrCreate(result.Value);
            // key：持久槽位 ID；value：该文件本次读取的容器与摘要详情。
            var detailsBySlotIdMap = new Dictionary<SaveSlotId, SaveSlotDetails>();
            foreach (SaveSlotDetails detail in result.Value)
            {
                SaveSlotDetails checkedDetail = detail;
                if (detail.SummaryResult.IsSuccess && detail.SummaryResult.Value.BusinessSummary is GameSaveSummary summary)
                {
                    try { summary.ValidateShape(); }
                    catch (InvalidOperationException exception)
                    {
                        checkedDetail = new SaveSlotDetails(detail.Entry,
                            SaveResult<SaveSlotSummary>.Failure(SaveErrorCode.InvalidSnapshot, exception.Message, exception));
                        WSLog.LogWarning("[GameSaveSystem] 一个队伍摘要异常，保留卡位并允许删除。");
                    }
                }
                detailsBySlotIdMap.Add(detail.Entry.SlotId, checkedDetail);
            }
            var refreshed = new List<GameSaveSlot>(10);
            for (int index = 0; index < slotIds.Count; index++)
            {
                detailsBySlotIdMap.TryGetValue(slotIds[index], out SaveSlotDetails detail);
                refreshed.Add(new GameSaveSlot(index, slotIds[index], detail));
            }
            slots = refreshed.AsReadOnly();
            Changed?.Invoke();
            WSLog.Log("[GameSaveSystem] 十个卡位摘要刷新完成。");
        }

        /// <summary>开始选定卡位，在进入庭院前恢复业务状态。</summary>
        /// <param name="index">零基卡位索引。</param>
        /// <param name="targetScene">庭院场景配置。</param>
        /// <returns>场景请求完成的异步任务。</returns>
        public async UniTask StartSlotAsync(int index, SceneLoadConfig targetScene)
        {
            if (State != E_GameSaveSessionState.Title) throw new InvalidOperationException("[GameSaveSystem] 当前不能重复进入游戏。");
            GameSaveSlot slot = slots[index];
            if (!slot.CanStart) throw new InvalidOperationException("[GameSaveSystem] 此存档摘要损坏，请检查或删除。");
            SetState(E_GameSaveSessionState.Starting);
            try
            {
                ResetBusinessState();
                if (!slot.IsEmpty)
                {
                    SaveResult<SaveLoadResult> loaded = await saveManager.LoadAsync(slot.SlotId);
                    if (loaded.IsSuccess) LastSavedAtUtcMilliseconds = loaded.Value.Summary.SavedAtUtcUnixMilliseconds;
                    if (!loaded.IsSuccess) throw new InvalidOperationException("[GameSaveSystem] 读档失败：" + loaded.Message, loaded.Exception);
                }
                pendingSlotId = slot.SlotId;
                StatusMessage = string.Empty;
                HasSaveFailure = false;
                WSLog.Log($"[GameSaveSystem] 已准备进入档位，slotNumber={index + 1}, newGame={slot.IsEmpty}。");
                await this.GetSystem<SceneLoadingSystem>().LoadAsync(targetScene);
            }
            catch
            {
                // 尚在标题且未创建 Player 时可以安全还原初始状态；场景已替换则保留加载失败界面。
                if (SceneManager.GetActiveScene().name == "GameStart" && PlayerController.Instance == null)
                {
                    try { ResetBusinessState(); pendingSlotId = default; SetState(E_GameSaveSessionState.Title); }
                    catch { SetState(E_GameSaveSessionState.Failed); throw; }
                }
                else SetState(E_GameSaveSessionState.Failed);
                throw;
            }
        }

        /// <summary>删除正式存档文件，卡位 GUID 与其他卡位保持不变。</summary>
        /// <param name="index">零基卡位索引。</param>
        /// <returns>删除并刷新后的异步任务。</returns>
        public async UniTask DeleteSlotAsync(int index)
        {
            if (State != E_GameSaveSessionState.Title) throw new InvalidOperationException("[GameSaveSystem] 游戏会话中不能删除卡位。");
            SaveResult result = await saveManager.DeleteAsync(slots[index].SlotId);
            if (!result.IsSuccess) throw new IOException("[GameSaveSystem] 删除失败：" + result.Message, result.Exception);
            await RefreshSlotsAsync();
            WSLog.Log($"[GameSaveSystem] 删除卡位文件完成，slotNumber={index + 1}。");
        }
        #endregion

        #region 保存与会话切换
        /// <summary>为场景 Task 与手动按钮提供统一保存入口，等待已有保存后采集最新状态。</summary>
        /// <param name="automatic">是否来自场景任务。</param>
        /// <param name="token">调用方取消令牌。</param>
        /// <returns>保存结果，未选档的调试启动会明确跳过自动保存。</returns>
        public async UniTask<SaveResult> SaveCurrentAsync(bool automatic = false, CancellationToken token = default)
        {
            SaveSlotId target = pendingSlotId.IsValid ? pendingSlotId : activeSlotId;
            if (!target.IsValid)
            {
                StatusMessage = "请从主界面选择存档后再保存。";
                Changed?.Invoke();
                WSLog.Log("[GameSaveSystem] 未选档的调试会话跳过保存。");
                return automatic ? SaveResult.Success() : SaveResult.Failure(SaveErrorCode.InvalidSlotId, StatusMessage);
            }
            if (IsSaving && !automatic) return SaveResult.Failure(SaveErrorCode.OperationBusy, "正在保存，请稍候。");
            await UniTask.WaitUntil(() => !IsSaving && !saveManager.IsBusy, cancellationToken: token);
            IsSaving = true;
            StatusMessage = "正在保存…";
            Changed?.Invoke();
            try
            {
                SaveResult result = await saveManager.SaveAsync(new SaveRequest(target, CaptureBusinessSummary()), token);
                if (result.IsSuccess) LastSavedAtUtcMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                HasSaveFailure = !result.IsSuccess;
                StatusMessage = result.IsSuccess ? $"存档 {FindSlotNumber(target)} 已保存" : "保存失败：" + result.Message;
                return result;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "保存已取消。";
                throw;
            }
            finally { IsSaving = false; Changed?.Invoke(); }
        }

        /// <summary>场景任务树完整成功后确认活动档，实际保存已经由末尾 Task 完成。</summary>
        public void ConfirmGameplayReady()
        {
            if (pendingSlotId.IsValid) { activeSlotId = pendingSlotId; pendingSlotId = default; }
            SetState(E_GameSaveSessionState.Playing);
        }

        /// <summary>先保存再返回 GameStart，保存失败保持原游戏和菜单。</summary>
        /// <returns>返回场景请求完成。</returns>
        public async UniTask ReturnToTitleAsync()
        {
            if (State != E_GameSaveSessionState.Playing) throw new InvalidOperationException("[GameSaveSystem] 当前不能返回主界面。");
            SetState(E_GameSaveSessionState.Returning);
            try
            {
                SaveResult saved = await SaveCurrentAsync();
                if (!saved.IsSuccess) { SetState(E_GameSaveSessionState.Playing); return; }
                await this.GetSystem<SceneLoadingSystem>().LoadAsync(TitleSceneId);
            }
            catch
            {
                SetState(PlayerController.Instance != null && SceneManager.GetActiveScene().name != "GameStart"
                    ? E_GameSaveSessionState.Playing : E_GameSaveSessionState.Failed);
                throw;
            }
        }

        /// <summary>在销毁窗口前暂停协调器并停止旧业务监听。</summary>
        public void BeginSessionCleanup()
        {
            SessionEnding?.Invoke();
            this.GetSystem<TaskSystem>().ClearPlayerTaskState();
            DialogueSession dialogue = this.GetSystem<DialogueSystem>().CurrentSession;
            if (dialogue != null && !dialogue.IsEnded) dialogue.End("返回主界面，中断当前对话。");
            WSLog.Log("[GameSaveSystem] 已停止旧会话监听，开始清理表现对象。");
        }

        /// <summary>Player 销毁后恢复初始业务状态，并重新连接窗口协调器。</summary>
        public void CompleteSessionCleanup()
        {
            ResetBusinessState();
            LastSavedAtUtcMilliseconds = 0;
            activeSlotId = default;
            pendingSlotId = default;
            StatusMessage = string.Empty;
            HasSaveFailure = false;
            SetState(E_GameSaveSessionState.Title);
            SessionEnded?.Invoke();
            WSLog.Log("[GameSaveSystem] 旧会话已清理，可以重新选档。");
        }
        #endregion

        #region 内部辅助
        /// <summary>创建只含稳定身份与保存时名称的四槽摘要。</summary>
        /// <returns>RPG 业务摘要。</returns>
        private GameSaveSummary CaptureBusinessSummary()
        {
            var summary = new GameSaveSummary();
            for (int index = 0; index < CharacterParty.SlotCount; index++)
            {
                CharacterId id = partyManager.GetCharacterIdAtSlot(index);
                summary.PartyMembers.Add(new GameSavePartyMemberSummary
                {
                    CharacterId = id.IsValid ? id.ToString() : string.Empty,
                    CharacterName = id.IsValid ? CharacterConfigManager.Instance.GetRequiredConfig(id).Name : string.Empty
                });
            }
            return summary;
        }

        /// <summary>先停止旧任务监听，再恢复所有初始模块，避免旧监听观察新档数据。</summary>
        private void ResetBusinessState()
        {
            this.GetSystem<TaskSystem>().ClearPlayerTaskState();
            saveManager.RestoreInitialState();
            this.GetSystem<TaskSystem>().RebuildRuntimes();
        }

        /// <summary>查找对外展示的一基卡位编号。</summary>
        /// <param name="slotId">槽位 ID。</param>
        /// <returns>卡位编号，无选档时为零。</returns>
        private int FindSlotNumber(SaveSlotId slotId)
        {
            if (slotIds == null || !slotId.IsValid) return 0;
            for (int index = 0; index < slotIds.Count; index++) if (slotIds[index] == slotId) return index + 1;
            return 0;
        }

        /// <summary>仅在会话阶段实际改变时发送通知。</summary>
        /// <param name="state">新阶段。</param>
        private void SetState(E_GameSaveSessionState state)
        {
            if (State == state) return;
            State = state;
            WSLog.Log($"[GameSaveSystem] 会话状态变化，state={state}。");
            Changed?.Invoke();
        }
        #endregion
    }
}
