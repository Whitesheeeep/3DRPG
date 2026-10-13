using RPG.Game.UI.Views.HUD;
using RPG.NPC;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>把一个 NPC 的序列化 ASC Health 和实际等级绑定到已打开 HUD。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 Inspector 显式绑定的 Boss GameplayAbilitySystemComponent 与同 NPC 的 NPCController。组件不查找 ASC，也不负责初始化 NPC 或 UI。")]
    public sealed class BossHealthHUDBinding : MonoBehaviour
    {
        #region 依赖字段

        // 由 Boss Prefab 明确指定数据源与等级来源，避免运行时按场景名称查找对象。
        [SerializeField, Required] private GameplayAbilitySystemComponent abilitySystemComponent;
        [SerializeField, Required] private NPCController npcController;

        #endregion

        #region 运行时绑定状态

        // 当前展示绑定的 ASC 与 HUD 均由本组件在启用期间订阅和释放。
        private UIManager uiManager;
        private HUDWindow hudWindow;
        private bool isObservingAbilitySystem;
        private bool invalidAttributesLogged;

        #endregion

        #region Unity 生命周期

        /// <summary>订阅 NPC 与 HUD 生命周期，并在 NPC 已初始化时立即读取 ASC 快照。</summary>
        private void OnEnable()
        {
            if (abilitySystemComponent == null || npcController == null)
            {
                Debug.LogError(
                    $"[BossHealthHUDBinding] '{name}' 缺少序列化 ASC 或 NPCController，Boss 血条保持隐藏。",
                    this);
                return;
            }

            npcController.Initialized += HandleNpcInitialized;
            SubscribeToUIManager();
            if (npcController.IsInitialized)
                BeginHealthObservation();
        }

        /// <summary>组件停用时解除 NPC、ASC 与 UIManager 订阅，并清除本来源的显示。</summary>
        private void OnDisable()
        {
            ReleaseBindings("component disabled");
        }

        /// <summary>销毁时再次执行幂等释放，覆盖未按预期触发的外部生命周期顺序。</summary>
        private void OnDestroy()
        {
            ReleaseBindings("component destroyed");
        }

        #endregion

        #region NPC 与 ASC 绑定

        /// <summary>响应所属 NPC 完整初始化后开始观察属性。</summary>
        /// <param name="initializedController">发布初始化事件的 NPCController。</param>
        private void HandleNpcInitialized(NPCController initializedController)
        {
            if (!ReferenceEquals(initializedController, npcController))
                return;

            BeginHealthObservation();
        }

        /// <summary>校验 ASC 所有者并订阅 Health 变化，再立即刷新当前血量快照。</summary>
        private void BeginHealthObservation()
        {
            if (isObservingAbilitySystem)
            {
                RefreshBossHealth(true);
                return;
            }

            if (!ReferenceEquals(abilitySystemComponent.Owner, npcController.Actor))
            {
                Debug.LogError(
                    $"[BossHealthHUDBinding] '{name}' 配置的 ASC 不属于 NPCController '{npcController.name}'。",
                    this);
                ClearBossHealthFromHud();
                return;
            }
            if (!abilitySystemComponent.IsInitialized || abilitySystemComponent.IsClearingRuntime)
                return;

            // 先订阅再读快照，保证同一主线程中后续 Health/MaxHealth 变更由事件驱动刷新。
            abilitySystemComponent.AttributeChanged += HandleAttributeChanged;
            abilitySystemComponent.RuntimeCleared += HandleRuntimeCleared;
            isObservingAbilitySystem = true;
            invalidAttributesLogged = false;
            Debug.Log(
                $"[BossHealthHUDBinding] '{name}' 已订阅 ASC Health 生命周期，level={npcController.Level}。",
                this);
            RefreshBossHealth(true);
        }

        /// <summary>只在 Health 或 MaxHealth 变化时读取完整快照并更新 HUD 比例。</summary>
        /// <param name="attribute">发生 CurrentValue 变化的 Attribute。</param>
        /// <param name="previousValue">结算前数值。</param>
        /// <param name="currentValue">结算后数值。</param>
        private void HandleAttributeChanged(GameplayAttribute attribute, float previousValue, float currentValue)
        {
            if (!attribute.Equals(GameplayAttributes.Attribute_Health) &&
                !attribute.Equals(GameplayAttributes.Attribute_MaxHealth))
                return;

            RefreshBossHealth(false);
        }

        /// <summary>ASC 清理运行时后隐藏血条并解除对该运行时的属性订阅。</summary>
        /// <param name="clearedAbilitySystem">完成清理的 ASC。</param>
        private void HandleRuntimeCleared(GameplayAbilitySystemComponent clearedAbilitySystem)
        {
            if (!ReferenceEquals(clearedAbilitySystem, abilitySystemComponent))
                return;

            Debug.Log($"[BossHealthHUDBinding] '{name}' 收到 ASC RuntimeCleared，隐藏 Boss 血条。", this);
            StopHealthObservation();
            ClearBossHealthFromHud();
        }

        /// <summary>读取 Health 与 MaxHealth；死亡、缺失属性或清理状态都会隐藏当前来源。</summary>
        /// <param name="mayClaimHud">首次绑定或 HUD 重建时允许取得显示所有权；普通属性更新只刷新当前所有者。</param>
        private void RefreshBossHealth(bool mayClaimHud)
        {
            if (!isObservingAbilitySystem || abilitySystemComponent.IsClearingRuntime)
                return;

            bool hasHealth = abilitySystemComponent.TryGetCurrentValue(
                GameplayAttributes.Attribute_Health,
                out float health);
            bool hasMaxHealth = abilitySystemComponent.TryGetCurrentValue(
                GameplayAttributes.Attribute_MaxHealth,
                out float maxHealth);
            if (!hasHealth || !hasMaxHealth)
            {
                if (!invalidAttributesLogged)
                {
                    Debug.LogError(
                        $"[BossHealthHUDBinding] NPC '{npcController.name}' 的 ASC 缺少 Health 或 MaxHealth，Boss 血条保持隐藏。",
                        this);
                    invalidAttributesLogged = true;
                }
                StopHealthObservation();
                ClearBossHealthFromHud();
                return;
            }

            invalidAttributesLogged = false;
            if (health <= 0f)
            {
                Debug.Log($"[BossHealthHUDBinding] NPC '{npcController.name}' Health 已归零，隐藏 Boss 血条。", this);
                StopHealthObservation();
                ClearBossHealthFromHud();
                return;
            }

            BindOrRefreshHud(health, maxHealth, mayClaimHud);
        }

        /// <summary>解除当前 ASC 的两个同步事件，并标记本组件不再展示该运行时。</summary>
        private void StopHealthObservation()
        {
            if (!isObservingAbilitySystem)
                return;

            abilitySystemComponent.AttributeChanged -= HandleAttributeChanged;
            abilitySystemComponent.RuntimeCleared -= HandleRuntimeCleared;
            isObservingAbilitySystem = false;
            Debug.Log($"[BossHealthHUDBinding] '{name}' 已解除 ASC Health 生命周期订阅。", this);
        }

        #endregion

        #region HUD 窗口生命周期

        /// <summary>订阅全局 HUD 窗口打开、隐藏和销毁事件以支持窗口晚创建与重建。</summary>
        private void SubscribeToUIManager()
        {
            uiManager = UIManager.Instance;
            uiManager.WindowOpened += HandleWindowOpened;
            uiManager.WindowHidden += HandleWindowHidden;
            uiManager.WindowDestroyed += HandleWindowDestroyed;
            if (uiManager.IsInitialized)
                BindOrRefreshHudFromSnapshot();
            Debug.Log($"[BossHealthHUDBinding] '{name}' 已订阅 HUD 窗口生命周期。", this);
        }

        /// <summary>HUD 显示时按当前 ASC 快照重新绑定，覆盖窗口隐藏期间的属性变化。</summary>
        /// <param name="snapshot">已完成显示的窗口快照。</param>
        private void HandleWindowOpened(UIWindowSnapshot snapshot)
        {
            if (snapshot.WindowName == nameof(HUDWindow))
                RefreshBossHealth(true);
        }

        /// <summary>HUD 隐藏时清空其当前 Boss 来源，窗口重新显示时会重新绑定。</summary>
        /// <param name="snapshot">已完成隐藏的窗口快照。</param>
        private void HandleWindowHidden(UIWindowSnapshot snapshot)
        {
            if (snapshot.WindowName == nameof(HUDWindow))
                ClearBossHealthFromHud();
        }

        /// <summary>HUD 销毁时清除旧引用，并在已有新实例时立即绑定新窗口。</summary>
        /// <param name="snapshot">已销毁的窗口快照。</param>
        private void HandleWindowDestroyed(UIWindowSnapshot snapshot)
        {
            if (snapshot.WindowName != nameof(HUDWindow))
                return;

            ClearBossHealthFromHud();
            hudWindow = null;
            if (uiManager.IsInitialized)
                BindOrRefreshHudFromSnapshot();
        }

        /// <summary>若 HUD 已创建，则绑定或刷新该窗口中的 Boss 血条。</summary>
        private void BindOrRefreshHudFromSnapshot()
        {
            if (!uiManager.TryGetWindow(out HUDWindow currentHudWindow))
                return;

            hudWindow = currentHudWindow;
            if (isObservingAbilitySystem)
                RefreshBossHealth(true);
        }

        /// <summary>把当前 ASC 快照写入现有 HUD，并保留 ASC 作为显示所有权标识。</summary>
        /// <param name="health">ASC 当前 Health。</param>
        /// <param name="maxHealth">ASC 当前 MaxHealth。</param>
        /// <param name="mayClaimHud">当当前 ASC 尚未拥有显示时，是否允许将其绑定为血条来源。</param>
        private void BindOrRefreshHud(float health, float maxHealth, bool mayClaimHud)
        {
            if (uiManager == null || !uiManager.IsInitialized)
                return;
            if (hudWindow == null && !uiManager.TryGetWindow(out hudWindow))
                return;

            bool refreshedCurrentSource = hudWindow.RefreshBossHealth(abilitySystemComponent, health, maxHealth);
            if (!refreshedCurrentSource && mayClaimHud)
                hudWindow.BindBossHealth(abilitySystemComponent, npcController.Level, health, maxHealth);
        }

        /// <summary>只清理由本组件 ASC 所有的当前血条，随后释放窗口对象引用。</summary>
        private void ClearBossHealthFromHud()
        {
            if (hudWindow != null && abilitySystemComponent != null)
                hudWindow.ClearBossHealth(abilitySystemComponent);
            hudWindow = null;
        }

        /// <summary>解除 NPC 与 UIManager 事件，并释放 ASC 观察及 HUD 显示所有权。</summary>
        /// <param name="reason">触发释放的生命周期原因。</param>
        private void ReleaseBindings(string reason)
        {
            if (npcController != null)
                npcController.Initialized -= HandleNpcInitialized;
            StopHealthObservation();
            ClearBossHealthFromHud();
            if (uiManager != null)
            {
                uiManager.WindowOpened -= HandleWindowOpened;
                uiManager.WindowHidden -= HandleWindowHidden;
                uiManager.WindowDestroyed -= HandleWindowDestroyed;
                uiManager = null;
                Debug.Log($"[BossHealthHUDBinding] '{name}' 已解除 HUD 窗口生命周期订阅，reason={reason}。", this);
            }
        }

        #endregion
    }
}
