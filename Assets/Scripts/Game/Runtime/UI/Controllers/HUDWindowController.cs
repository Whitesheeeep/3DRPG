using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Character;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.HUD;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>把 HUD 展示与当前队伍各角色的 ASC 属性及切换生命周期连接起来。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUD 根节点的 HUDWindowDataComponent、Prefab 内静态 HUDHealthView 和当前场景 PlayerController。没有 PlayerController 时血量区域保持空白，并在 HUD 再次显示时重试绑定。")]
    public sealed class HUDWindowController : MonoBehaviour
    {
        #region 配置与依赖字段

        // Inspector 显式绑定的 Prefab View，以及角色业务数据源。
        [SerializeField, Required] private HUDHealthView healthView;
        private HUDWindowDataComponent windowData;
        private PlayerController playerController;
        private CharacterManager characterManager;
        private CharacterPartyManager partyManager;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private Button characterButton;
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
                windowData.DocumentUIPanelDocumentUIPanel.CharacterButton == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindowDataComponent 未绑定 BagButton 或 CharacterButton。");
            if (healthView == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindow Prefab 未绑定静态 HUDHealthView。");
            healthView.ValidateConfiguration();
            healthView.Clear();
            characterButton = windowData.DocumentUIPanelDocumentUIPanel.CharacterButton;
            characterButton.onClick.AddListener(HandleCharacterButtonClicked);
            initialized = true;
            TryBindRuntimeSources();
            WSLog.Log("[HUDWindowController] HUD 血量视图初始化完成。");
        }

        /// <summary>窗口显示时重试场景依赖绑定并读取最新属性快照。</summary>
        public void HandleWindowShown()
        {
            if (!initialized || disposed) return;
            TryBindRuntimeSources();
            if (characterManager != null && characterManager.IsReady)
            {
                RefreshAllViews();
                if (spriteAtlasLeaseService != null)
                    LoadConfiguredAtlasesAsync().Forget(HandleAtlasLoadException);
            }
        }

        /// <summary>解除角色、队伍和 ASC 事件订阅，并释放 HUD 持有的头像图集引用。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (characterManager != null)
            {
                characterManager.Initialized -= HandleCharacterManagerInitialized;
                characterManager.ActiveCharacterChanged -= HandleActiveCharacterChanged;
            }
            if (partyManager != null) partyManager.Changed -= HandlePartyChanged;
            characterButton?.onClick.RemoveListener(HandleCharacterButtonClicked);
            characterButton = null;
            UnbindCharacterAttributes();
            if (spriteAtlasLeaseService != null)
                spriteAtlasLeaseService.Released -= HandleAtlasesReleased;
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            healthView = null;
            characterManager = null;
            partyManager = null;
            playerController = null;
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

        #endregion

        #region 运行时数据绑定

        /// <summary>绑定当前场景的 PlayerController、队伍管理器及角色生命周期事件。</summary>
        /// <returns>找到并绑定 PlayerController 时返回 true。</returns>
        private bool TryBindRuntimeSources()
        {
            if (characterManager != null) return true;
            playerController = FindFirstObjectByType<PlayerController>();
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

        /// <summary>读取当前 ASC 快照并刷新 Active 主血条及全部队伍槽位。</summary>
        private void RefreshAllViews()
        {
            if (healthView == null || characterManager == null || !characterManager.IsReady)
            {
                healthView?.Clear();
                return;
            }

            CharacterActor activeActor = characterManager.ActiveCharacter;
            if (activeActor == null)
                healthView.Clear();
            else
                RefreshActiveCharacter(activeActor);

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

        #endregion
    }
}
