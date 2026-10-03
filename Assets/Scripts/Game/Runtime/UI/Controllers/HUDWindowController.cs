using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Character;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.HUD;
using RPG.PlayerInputSystem;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>把 HUD 展示与当前队伍各角色的 ASC 属性及切换生命周期连接起来。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUD 根节点的 HUDWindowDataComponent 与 HUDTaskController、Prefab 内静态血量和技能格 View，以及 PlayerController.Instance。TaskButton 红点由 Prefab 中 RedDotUGUIBadge 直接显示；HUDTaskController 在显示期间读取追踪任务并投影可选目标。")]
    public sealed class HUDWindowController : MonoBehaviour
    {
        #region 配置与依赖字段

        // Inspector 显式绑定的 Prefab View，以及角色业务数据源。
        [SerializeField, Required] private HUDHealthView healthView;
        [SerializeField, Required] private HUDSkillSlotView[] skillSlotViews = new HUDSkillSlotView[4];
        [SerializeField, Required] private HUDTaskController taskController;
        private HUDWindowDataComponent windowData;
        private PlayerController playerController;
        private CharacterManager characterManager;
        private CharacterPartyManager partyManager;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private Button characterButton;
        private CharacterActor cooldownBoundActor;
        private IGameplayAbilityCtrl cooldownAbilityController;
        // 固定四格分别对应 Sprint Click 与 Skill2-4；不使用旧的 Skill1 输入类型。
        private static readonly E_PlayerInputType[] SkillSlotInputTypes =
        {
            E_PlayerInputType.Sprint,
            E_PlayerInputType.Skill2,
            E_PlayerInputType.Skill3,
            E_PlayerInputType.Skill4
        };
        private readonly GameplayAbilityData[] abilityDataBySlot = new GameplayAbilityData[4];
        private readonly GameEffectRuntime[] cooldownRuntimeBySlot = new GameEffectRuntime[4];
        // ASC 订阅和固定队伍槽位快照。
        // key：CharacterActor；value：订阅该角色 ASC AttributeChanged 的委托，用于 Dispose 时精确解绑。
        private readonly Dictionary<CharacterActor, Action<GameplayAttribute, float, float>>
            attributeChangedHandlerByActorMap = new();
        private readonly HashSet<CharacterActor> invalidHealthAttributeLoggedActors = new();
        private readonly CharacterActor[] characterBySlot = new CharacterActor[CharacterParty.SlotCount];
        private bool initialized;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建 HUD 视图并开始等待角色队伍 Ready。</summary>
        public void Initialize()
        {
            if (initialized) return;
            windowData = GetComponent<HUDWindowDataComponent>();
            if (windowData == null)
                throw new InvalidOperationException("[HUDWindowController] HUD 根节点缺少 HUDWindowDataComponent。");
            if (windowData.BagButton == null || windowData.DocumentUIPanelDocumentUIPanel == null ||
                windowData.DocumentUIPanelDocumentUIPanel.CharacterButton == null ||
                windowData.DocumentUIPanelDocumentUIPanel.TaskButton == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindowDataComponent 未绑定 BagButton、CharacterButton 或 TaskButton。");
            if (healthView == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindow Prefab 未绑定静态 HUDHealthView。");
            healthView.ValidateConfiguration();
            ValidateSkillSlotViews();
            if (taskController == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindow Prefab 未绑定 HUDTaskController。");
            healthView.Clear();
            ClearSkillSlots();
            taskController.Initialize();
            characterButton = windowData.DocumentUIPanelDocumentUIPanel.CharacterButton;
            characterButton.onClick.AddListener(HandleCharacterButtonClicked);
            windowData.DocumentUIPanelDocumentUIPanel.TaskButton.onClick.AddListener(HandleTaskButtonClicked);
            initialized = true;
            TryBindRuntimeSources();
            WSLog.Log("[HUDWindowController] HUD 血量视图初始化完成。");
        }

        /// <summary>窗口显示时重试场景依赖绑定并读取最新属性快照。</summary>
        public void HandleWindowShown()
        {
            if (!initialized || disposed) return;
            taskController.OnWindowShown();
            TryBindRuntimeSources();
            if (characterManager != null && characterManager.IsReady)
            {
                RefreshAllViews();
                if (spriteAtlasLeaseService != null)
                    LoadConfiguredAtlasesAsync().Forget(HandleAtlasLoadException);
            }
        }

        /// <summary>窗口隐藏时停止任务事实订阅并隐藏追踪摘要与世界标记。</summary>
        public void HandleWindowHidden()
        {
            taskController?.OnWindowHidden();
        }

        /// <summary>向 HUD 任务控制器设置测试或玩法提供的导航目标。</summary>
        /// <param name="taskId">目标所属任务标识。</param>
        /// <param name="target">目标世界 Transform。</param>
        /// <param name="offset">相对目标原点的世界坐标偏移。</param>
        public void SetTaskNavigationTarget(TaskId taskId, Transform target, Vector3 offset)
        {
            taskController.SetNavigationTarget(taskId, target, offset);
        }

        /// <summary>清除 HUD 当前使用的导航目标输入。</summary>
        public void ClearTaskNavigationTarget()
        {
            if (disposed)
            {
                WSLog.Log("[HUDWindowController] HUD 已释放，忽略迟到的任务导航清理请求。");
                return;
            }

            taskController.ClearNavigationTarget();
        }

        /// <summary>解除角色、队伍和 ASC 事件订阅，并释放 HUD 持有的头像图集引用。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            taskController?.Dispose();
            if (characterManager != null)
            {
                characterManager.Initialized -= HandleCharacterManagerInitialized;
                characterManager.ActiveCharacterChanged -= HandleActiveCharacterChanged;
            }
            if (partyManager != null) partyManager.Changed -= HandlePartyChanged;
            characterButton?.onClick.RemoveListener(HandleCharacterButtonClicked);
            characterButton = null;
            if (windowData != null && windowData.DocumentUIPanelDocumentUIPanel != null &&
                windowData.DocumentUIPanelDocumentUIPanel.TaskButton != null)
                windowData.DocumentUIPanelDocumentUIPanel.TaskButton.onClick.RemoveListener(HandleTaskButtonClicked);
            UnbindCharacterAttributes();
            UnbindActiveCharacterCooldowns();
            ClearSkillSlots();
            if (spriteAtlasLeaseService != null)
                spriteAtlasLeaseService.Released -= HandleAtlasesReleased;
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            healthView = null;
            characterManager = null;
            partyManager = null;
            playerController = null;
            taskController = null;
            WSLog.Log("[HUDWindowController] HUD 血量绑定与图集租约已释放。");
        }

        #endregion

        #region HUD 用户意图

        /// <summary>将 HUD Bag 按钮意图发布为统一的背包打开事件。</summary>
        /// <exception cref="InvalidOperationException">HUD Controller 尚未由 HUDWindow 初始化时抛出。</exception>
        public void HandleBagButtonClicked()
        {
            if (!initialized)
                throw new InvalidOperationException("[HUDWindowController] 尚未初始化，无法处理 Bag 按钮。");

            WSLog.Log("[HUDWindowController] 点击 HUD Bag 按钮，发布背包打开请求。");
            EventSystem.EventTrigger_Type(
                typeof(BagWindowOpenRequestedEventArgs),
                new BagWindowOpenRequestedEventArgs(BagWindowRequestSource.HudButton));
        }

        /// <summary>将 HUD 角色按钮意图发布为统一角色窗口打开事件。</summary>
        public void HandleCharacterButtonClicked()
        {
            if (!initialized)
                throw new InvalidOperationException("[HUDWindowController] 尚未初始化，无法处理 Character 按钮。");
            WSLog.Log("[HUDWindowController] 点击 HUD Character 按钮，发布角色窗口打开请求。");
            EventSystem.EventTrigger_Type(
                typeof(CharacterWindowOpenRequestedEventArgs),
                new CharacterWindowOpenRequestedEventArgs(CharacterWindowOpenSource.HudButton));
        }

        /// <summary>将 HUD 任务按钮点击转换为统一任务窗口打开意图。</summary>
        public void HandleTaskButtonClicked()
        {
            if (!initialized)
                throw new InvalidOperationException("[HUDWindowController] 尚未初始化，无法处理 Task 按钮。");
            WSLog.Log("[HUDWindowController] 点击 HUD Task 按钮，发布任务窗口打开请求。");
            EventSystem.EventTrigger_Type(
                typeof(RPG.Game.UI.Task.TaskWindowOpenRequestedEventArgs),
                new RPG.Game.UI.Task.TaskWindowOpenRequestedEventArgs());
        }

        #endregion

        #region 运行时数据绑定

        /// <summary>绑定当前场景的 PlayerController、队伍管理器及角色生命周期事件。</summary>
        /// <returns>找到并绑定 PlayerController 时返回 true。</returns>
        private bool TryBindRuntimeSources()
        {
            if (characterManager != null) return true;
            playerController = PlayerController.Instance;
            if (playerController == null) return false;

            characterManager = playerController.CharacterManager;
            partyManager = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
            characterManager.Initialized += HandleCharacterManagerInitialized;
            characterManager.ActiveCharacterChanged += HandleActiveCharacterChanged;
            partyManager.Changed += HandlePartyChanged;
            WSLog.Log("[HUDWindowController] 已绑定 PlayerController、CharacterManager 与 CharacterPartyManager。");

            if (characterManager.IsReady) RebuildPartyBindings();
            return true;
        }

        /// <summary>在队伍 Actor 全部初始化后订阅 ASC 并刷新固定槽位。</summary>
        private void HandleCharacterManagerInitialized()
        {
            if (disposed) return;
            RebuildPartyBindings();
        }

        /// <summary>在队伍槽位数据变化后重新匹配已加载 Actor。</summary>
        private void HandlePartyChanged()
        {
            if (disposed || characterManager == null || !characterManager.IsReady) return;
            RebuildPartyBindings();
        }

        /// <summary>按固定四槽位快照重建角色属性订阅和头像图集租约。</summary>
        private void RebuildPartyBindings()
        {
            UnbindCharacterAttributes();
            if (spriteAtlasLeaseService != null)
                spriteAtlasLeaseService.Released -= HandleAtlasesReleased;
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            var atlasAddresses = new List<string>(CharacterParty.SlotCount);
            IReadOnlyList<CharacterId> characterIdsBySlot = partyManager.CreateSnapshot();

            for (int slotIndex = 0; slotIndex < CharacterParty.SlotCount; slotIndex++)
            {
                CharacterId characterId = characterIdsBySlot[slotIndex];
                if (!characterId.IsValid)
                {
                    characterBySlot[slotIndex] = null;
                    healthView.ClearPartySlot(slotIndex);
                    continue;
                }

                CharacterActor actor = characterManager.Find(characterId);
                characterBySlot[slotIndex] = actor;
                if (actor == null)
                {
                    healthView.ClearPartySlot(slotIndex);
                    Debug.LogWarning($"[HUDWindowController] 队伍槽位 {slotIndex + 1} 的角色尚未加载为 Actor，character={characterId}。", this);
                    continue;
                }

                Action<GameplayAttribute, float, float> handler =
                    (attribute, oldValue, newValue) => HandleActorAttributeChanged(actor, attribute);
                actor.AbilitySystemComponent.AttributeChanged += handler;
                attributeChangedHandlerByActorMap.Add(actor, handler);
                string atlasAddress = actor.Config.SideIconAddress;
                if (!string.IsNullOrWhiteSpace(atlasAddress) && !atlasAddresses.Contains(atlasAddress))
                    atlasAddresses.Add(atlasAddress);
            }

            if (atlasAddresses.Count > 0)
            {
                spriteAtlasLeaseService = new WindowSpriteAtlasLeaseService(atlasAddresses, 0f, "HUDWindow");
                spriteAtlasLeaseService.Released += HandleAtlasesReleased;
                LoadConfiguredAtlasesAsync().Forget(HandleAtlasLoadException);
            }

            RefreshAllViews();
            WSLog.Log($"[HUDWindowController] 已按队伍槽位绑定角色 ASC，actorCount={attributeChangedHandlerByActorMap.Count}。");
        }

        /// <summary>移除当前 HUD 注册的所有角色 ASC 属性回调。</summary>
        private void UnbindCharacterAttributes()
        {
            foreach (KeyValuePair<CharacterActor, Action<GameplayAttribute, float, float>> pair
                     in attributeChangedHandlerByActorMap)
                if (pair.Key != null)
                    pair.Key.AbilitySystemComponent.AttributeChanged -= pair.Value;
            attributeChangedHandlerByActorMap.Clear();
            invalidHealthAttributeLoggedActors.Clear();
            Array.Clear(characterBySlot, 0, characterBySlot.Length);
        }

        #endregion

        #region 属性与切换事件

        /// <summary>在 Active 角色切换后重绘主血条和当前槽位高亮。</summary>
        /// <param name="previous">先前 Active 角色。</param>
        /// <param name="current">新的 Active 角色。</param>
        private void HandleActiveCharacterChanged(CharacterActor previous, CharacterActor current)
        {
            if (!disposed) RefreshAllViews();
        }

        /// <summary>只在角色 Health 或 MaxHealth 变化时刷新该角色对应的 UI。</summary>
        /// <param name="actor">变化属性所属角色。</param>
        /// <param name="attribute">发生变化的 GAS Attribute。</param>
        private void HandleActorAttributeChanged(CharacterActor actor, GameplayAttribute attribute)
        {
            if (disposed || actor == null ||
                (attribute != GameplayAttributes.Attribute_Health &&
                 attribute != GameplayAttributes.Attribute_MaxHealth))
                return;
            RefreshActor(actor);
        }

        /// <summary>处理头像 Atlas 实际释放后清空视图中的 Sprite 引用。</summary>
        private void HandleAtlasesReleased()
        {
            if (!disposed) RefreshAllViews();
        }

        /// <summary>记录头像图集加载未全部成功的上下文，血条和名称仍可用。</summary>
        /// <param name="exception">加载任务异常。</param>
        private void HandleAtlasLoadException(Exception exception)
        {
            if (exception != null)
                Debug.LogWarning($"[HUDWindowController] 角色头像图集加载任务失败，名称与血量仍保持显示：{exception.Message}", this);
        }

        /// <summary>加载角色侧面头像图集，并在失败时保留无头像的血量 UI。</summary>
        private async UniTask LoadConfiguredAtlasesAsync()
        {
            bool succeeded = await spriteAtlasLeaseService.BeginLoadConfiguredAtlasesAsync();
            if (disposed) return;
            if (!succeeded)
                WSLog.LogWarning("[HUDWindowController] 部分角色头像图集加载失败，血量和名称仍保持显示。");
            RefreshAllViews();
        }

        #endregion

        #region 视图刷新

        /// <summary>按技能格缓存的冷却 Runtime 刷新剩余时间显示。</summary>
        private void Update()
        {
            if (initialized && !disposed && cooldownBoundActor != null)
                RefreshCooldownViews();
        }

        /// <summary>读取当前 ASC 快照并刷新 Active 主血条及全部队伍槽位。</summary>
        private void RefreshAllViews()
        {
            // 清楚所有视图
            if (healthView == null || characterManager == null || !characterManager.IsReady)
            {
                healthView?.Clear();
                ClearSkillSlots();
                UnbindActiveCharacterCooldowns();
                return;
            }

            CharacterActor activeActor = characterManager.ActiveCharacter;
            if (activeActor == null)
            {
                healthView.Clear();
                ClearSkillSlots();
                UnbindActiveCharacterCooldowns();
            }
            else
            {
                RefreshActiveCharacter(activeActor);
                RefreshSkillSlots(activeActor);
            }

            for (int slotIndex = 0; slotIndex < characterBySlot.Length; slotIndex++)
            {
                CharacterActor actor = characterBySlot[slotIndex];
                if (actor == null)
                {
                    healthView.ClearPartySlot(slotIndex);
                    continue;
                }
                RefreshPartySlot(slotIndex, actor);
            }
        }

        /// <summary>刷新指定角色涉及的底部主血条和对应队伍槽位。</summary>
        /// <param name="actor">需要刷新的角色。</param>
        private void RefreshActor(CharacterActor actor)
        {
            if (!TryReadHealth(actor, out float health, out float maxHealth))
            {
                health = 0f;
                maxHealth = 0f;
            }

            if (ReferenceEquals(characterManager.ActiveCharacter, actor))
                healthView.SetActiveCharacter(actor.Instance.Level, health, maxHealth);

            for (int slotIndex = 0; slotIndex < characterBySlot.Length; slotIndex++)
                if (ReferenceEquals(characterBySlot[slotIndex], actor))
                    healthView.SetPartySlot(slotIndex, actor.Config.Name, GetCharacterPortrait(actor),
                        health, maxHealth, ReferenceEquals(characterManager.ActiveCharacter, actor));
        }

        /// <summary>刷新底部 Active 角色的显示内容。</summary>
        /// <param name="actor">当前 Active 角色。</param>
        private void RefreshActiveCharacter(CharacterActor actor)
        {
            if (!TryReadHealth(actor, out float health, out float maxHealth))
            {
                health = 0f;
                maxHealth = 0f;
            }
            healthView.SetActiveCharacter(actor.Instance.Level, health, maxHealth);
        }

        /// <summary>刷新指定固定槽位的血条、名称、头像和 Active 高亮。</summary>
        /// <param name="slotIndex">零基固定槽位。</param>
        /// <param name="actor">槽位角色。</param>
        private void RefreshPartySlot(int slotIndex, CharacterActor actor)
        {
            if (!TryReadHealth(actor, out float health, out float maxHealth))
            {
                health = 0f;
                maxHealth = 0f;
            }
            healthView.SetPartySlot(slotIndex, actor.Config.Name, GetCharacterPortrait(actor),
                health, maxHealth, ReferenceEquals(characterManager.ActiveCharacter, actor));
        }

        /// <summary>读取角色 ASC 当前 Health 和 MaxHealth，并对缺失配置记录一次错误。</summary>
        /// <param name="actor">属性所属角色。</param>
        /// <param name="health">ASC 当前 Health。</param>
        /// <param name="maxHealth">ASC 当前 MaxHealth。</param>
        /// <returns>两个 Attribute 均存在且读取成功时返回 true。</returns>
        private bool TryReadHealth(CharacterActor actor, out float health, out float maxHealth)
        {
            bool hasHealth = actor.AbilitySystemComponent.TryGetCurrentValue(
                GameplayAttributes.Attribute_Health, out health);
            bool hasMaxHealth = actor.AbilitySystemComponent.TryGetCurrentValue(
                GameplayAttributes.Attribute_MaxHealth, out maxHealth);
            if (!hasHealth || !hasMaxHealth)
            {
                if (invalidHealthAttributeLoggedActors.Add(actor))
                    Debug.LogError($"[HUDWindowController] 角色 ASC 缺少 Health 或 MaxHealth，character={actor.CharacterId}。", this);
                return false;
            }
            invalidHealthAttributeLoggedActors.Remove(actor);
            return true;
        }

        /// <summary>从已加载图集中获取角色侧面头像。</summary>
        /// <param name="actor">目标角色。</param>
        /// <returns>Atlas 中对应的 Sprite；未加载或缺少资源时为空。</returns>
        private Sprite GetCharacterPortrait(CharacterActor actor)
        {
            if (spriteAtlasLeaseService == null ||
                !spriteAtlasLeaseService.TryGetSprite(actor.Config.SideIconAddress,
                    actor.Config.SideIconSpriteName, out Sprite portrait))
                return null;
            return portrait;
        }

        /// <summary>验证四个静态技能格引用和其固定输入顺序。</summary>
        /// <exception cref="InvalidOperationException">数组长度、View 引用或输入顺序不符合 HUD 契约时抛出。</exception>
        private void ValidateSkillSlotViews()
        {
            if (skillSlotViews == null || skillSlotViews.Length != SkillSlotInputTypes.Length)
                throw new InvalidOperationException("[HUDWindowController] HUD 必须显式绑定四个静态技能格 View。");

            for (int slotIndex = 0; slotIndex < skillSlotViews.Length; slotIndex++)
            {
                HUDSkillSlotView view = skillSlotViews[slotIndex];
                if (view == null)
                    throw new InvalidOperationException(
                        $"[HUDWindowController] 技能格 {slotIndex + 1} 未绑定 HUDSkillSlotView。");
                view.ValidateConfiguration();
                if (view.InputType != SkillSlotInputTypes[slotIndex])
                    throw new InvalidOperationException(
                        $"[HUDWindowController] 技能格 {slotIndex + 1} 应绑定 {SkillSlotInputTypes[slotIndex]}，实际为 {view.InputType}。");
            }
        }

        /// <summary>将当前角色配置的 Sprint、Skill2、Skill3、Skill4 Ability 图标绑定到四个固定技能格。</summary>
        /// <param name="actor">当前 Active 角色。</param>
        private void RefreshSkillSlots(CharacterActor actor)
        {
            bool cooldownActorChanged = !ReferenceEquals(cooldownBoundActor, actor);
            BindActiveCharacterCooldowns(actor);
            IReadOnlyList<CharacterAbilityInputBinding> bindings = actor.Config.CombatConfig.SkillInputBindings;
            bool abilityBindingsChanged = false;
            for (int slotIndex = 0; slotIndex < skillSlotViews.Length; slotIndex++)
            {
                E_PlayerInputType inputType = skillSlotViews[slotIndex].InputType;
                GameplayAbilityData abilityData = null;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    CharacterAbilityInputBinding binding = bindings[bindingIndex];
                    if (binding.InputType != inputType) continue;
                    abilityData = binding.Ability;
                    break;
                }

                if (!ReferenceEquals(abilityDataBySlot[slotIndex], abilityData))
                    abilityBindingsChanged = true;
                abilityDataBySlot[slotIndex] = abilityData;
                skillSlotViews[slotIndex].SetAbilityIcon(abilityData?.Icon);
            }

            // 角色或槽位配置变化时做一次快照同步，之后的冷却变化由生命周期事件维护。
            if (cooldownActorChanged || abilityBindingsChanged)
                RefreshCooldownRuntimes();
        }

        /// <summary>订阅 Active 角色 ASC 冷却事件；冷却快照在技能格绑定后单独同步。</summary>
        /// <param name="actor">需要展示技能和冷却的 Active 角色。</param>
        private void BindActiveCharacterCooldowns(CharacterActor actor)
        {
            if (ReferenceEquals(cooldownBoundActor, actor)) return;
            UnbindActiveCharacterCooldowns();
            cooldownBoundActor = actor;
            cooldownAbilityController = actor.AbilitySystemComponent.Abilities;
            cooldownAbilityController.CooldownStarted += HandleCooldownStarted;
            cooldownAbilityController.CooldownEnded += HandleCooldownEnded;
            WSLog.Log($"[HUDWindowController] 已订阅 Active 角色冷却事件，character={actor.CharacterId}。");
        }

        /// <summary>解除 Active 角色的冷却事件订阅并丢弃旧角色的 Runtime 引用。</summary>
        private void UnbindActiveCharacterCooldowns()
        {
            if (cooldownAbilityController != null)
            {
                cooldownAbilityController.CooldownStarted -= HandleCooldownStarted;
                cooldownAbilityController.CooldownEnded -= HandleCooldownEnded;
            }

            if (cooldownBoundActor != null)
                WSLog.Log($"[HUDWindowController] 已解除 Active 角色冷却事件，character={cooldownBoundActor.CharacterId}。");
            cooldownAbilityController = null;
            cooldownBoundActor = null;
            Array.Clear(cooldownRuntimeBySlot, 0, cooldownRuntimeBySlot.Length);
        }

        /// <summary>把新建冷却 Runtime 增量写入对应 Ability 技能格。</summary>
        /// <param name="args">已创建并应用到 ASC 的冷却生命周期快照。</param>
        private void HandleCooldownStarted(GameplayAbilityCooldownEventArgs args)
        {
            if (disposed) return;

            int updatedSlotCount = 0;
            for (int slotIndex = 0; slotIndex < abilityDataBySlot.Length; slotIndex++)
            {
                if (!ReferenceEquals(abilityDataBySlot[slotIndex], args.AbilityData)) continue;
                cooldownRuntimeBySlot[slotIndex] = args.CooldownRuntime;
                updatedSlotCount++;
            }

            if (updatedSlotCount == 0) return;
            RefreshCooldownViews();
            WSLog.Log(
                $"[HUDWindowController] 技能冷却开始并更新技能格，ability={args.AbilityData.name}, duration={args.Duration}, slotCount={updatedSlotCount}。");
        }

        /// <summary>只清除引用与结束事件完全相同的技能格冷却 Runtime。</summary>
        /// <param name="args">结束事件携带的冷却生命周期快照。</param>
        private void HandleCooldownEnded(GameplayAbilityCooldownEventArgs args)
        {
            if (disposed) return;

            int clearedSlotCount = 0;
            for (int slotIndex = 0; slotIndex < cooldownRuntimeBySlot.Length; slotIndex++)
            {
                if (!ReferenceEquals(cooldownRuntimeBySlot[slotIndex], args.CooldownRuntime)) continue;
                cooldownRuntimeBySlot[slotIndex] = null;
                clearedSlotCount++;
            }

            if (clearedSlotCount == 0) return;
            RefreshCooldownViews();
            WSLog.Log(
                $"[HUDWindowController] 技能冷却结束并清除技能格，ability={args.AbilityData.name}, slotCount={clearedSlotCount}。");
        }

        /// <summary>在角色或技能槽配置变化时读取一次 Active Effects，恢复已开始的冷却。</summary>
        private void RefreshCooldownRuntimes()
        {
            Array.Clear(cooldownRuntimeBySlot, 0, cooldownRuntimeBySlot.Length);
            if (cooldownBoundActor == null)
            {
                RefreshCooldownViews();
                return;
            }

            IReadOnlyList<GameEffectRuntime> activeEffects = cooldownBoundActor.AbilitySystemComponent.ActiveEffects;
            // 首次绑定用冷却 GE 资产引用恢复状态；运行中的开始和结束由 GA 事件增量维护。
            for (int slotIndex = 0; slotIndex < abilityDataBySlot.Length; slotIndex++)
            {
                GameplayEffectData configuredCooldown = abilityDataBySlot[slotIndex]?.CooldownEffect;
                if (configuredCooldown == null) continue;

                for (int effectIndex = 0; effectIndex < activeEffects.Count; effectIndex++)
                {
                    GameEffectRuntime runtime = activeEffects[effectIndex];
                    if (!runtime.IsActive || !ReferenceEquals(runtime.Data, configuredCooldown)) continue;
                    cooldownRuntimeBySlot[slotIndex] = runtime;
                    break;
                }
            }

            RefreshCooldownViews();
        }

        /// <summary>把缓存的 Active GE 剩余时长转换为各 View 的圆形填充和数字文本。</summary>
        private void RefreshCooldownViews()
        {
            for (int slotIndex = 0; slotIndex < skillSlotViews.Length; slotIndex++)
            {
                GameEffectRuntime runtime = cooldownRuntimeBySlot[slotIndex];
                bool active = runtime != null && runtime.IsActive && abilityDataBySlot[slotIndex] != null;
                bool infinite = active && runtime.Data.DurationType == E_GameEffectDurationType.Infinite;
                skillSlotViews[slotIndex].RefreshCooldown(active,
                    active ? runtime.RemainingDuration : 0f,
                    active ? runtime.Data.Duration : 0f,
                    infinite);
            }
        }

        /// <summary>清空所有静态技能格内容，同时保留 Prefab 底板和布局。</summary>
        private void ClearSkillSlots()
        {
            Array.Clear(abilityDataBySlot, 0, abilityDataBySlot.Length);
            Array.Clear(cooldownRuntimeBySlot, 0, cooldownRuntimeBySlot.Length);
            if (skillSlotViews == null) return;
            for (int slotIndex = 0; slotIndex < skillSlotViews.Length; slotIndex++)
                skillSlotViews[slotIndex]?.Clear();
        }

        #endregion
    }
}
