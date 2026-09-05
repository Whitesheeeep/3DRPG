using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>管理常驻玩家队伍的 CharacterActor 集合与当前角色。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同一 CharacterRoot 子树中的 CharacterActor；CharacterRoot 必须由稳定 Player 持有并跨场景保留。")]
    public sealed class CharacterManager : MonoBehaviour
    {
        #region 配置与状态

        // CharacterRoot 下的角色来源；Manager 不持有 PlayerController 或 MotionDriver。
        [SerializeField] private Transform actorContainer;
        [SerializeField] private CharacterDefinition[] initialDefinitions = Array.Empty<CharacterDefinition>();
        [SerializeField] private CharacterId initialCharacterId;

        [SerializeField, ReadOnly, Tooltip("已通过 Marker 校验的队伍角色；仅用于调试。")]
        private readonly List<CharacterActor> characters = new();
        private bool initialized;
        // 固定槽位输入只在显式阶段调用时查询，不把 PlayerInputController 保存为 Manager 生命周期依赖。
        private static readonly PlayerInputType[] characterSlotInputTypes =
        {
            PlayerInputType.CharacterSlot1,
            PlayerInputType.CharacterSlot2,
            PlayerInputType.CharacterSlot3,
            PlayerInputType.CharacterSlot4
        };

        #endregion

        #region 事件与属性

        /// <summary>在 ActiveCharacter 完成同步切换后发送一次。</summary>
        public event Action<CharacterActor, CharacterActor> ActiveCharacterChanged;
        /// <summary>获取全部通过 Marker 校验的队伍角色。</summary>
        public IReadOnlyList<CharacterActor> Characters => characters;
        /// <summary>获取队伍是否已完成初始化；未初始化时切换请求返回 NotInitialized。</summary>
        public bool IsInitialized => initialized;
        /// <summary>获取当前由玩家操控的角色。</summary>
        public CharacterActor ActiveCharacter { get; private set; }

        #endregion

        #region 初始化与队伍操作

        /// <summary>构建常驻队伍并校验角色身份与 Marker；阶段推进由 PlayerController 显式调用。</summary>
        public void Initialize()
        {
            if (initialized) return;
            Transform container = actorContainer != null ? actorContainer : transform;

            // 先实例化配置角色，再统一扫描，允许场景直接放置测试角色而不创建 Definition。
            foreach (CharacterDefinition definition in initialDefinitions)
            {
                if (definition == null || definition.ActorPrefab == null)
                    throw new InvalidOperationException("[CharacterManager] 初始队伍包含空 Definition 或角色 Prefab。");
                CharacterActor instance = Instantiate(definition.ActorPrefab, container);
                instance.AssignIdentity(definition.CharacterId);
            }

            characters.Clear();
            foreach (CharacterActor actor in container.GetComponentsInChildren<CharacterActor>(true))
            {
                if (!actor.MarkerProvider.TryRebuild() || !actor.MarkerProvider.IsValid)
                {
                    Debug.LogError($"[CharacterManager] 角色 '{actor.name}' 的 MarkerProvider 校验失败，已拒绝加入队伍。", actor);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(actor.CharacterId.ToString()) || Find(actor.CharacterId) != null)
                    throw new InvalidOperationException($"[CharacterManager] 角色 '{actor.name}' 的 CharacterId 为空或重复。");
                actor.SetActivePresentation(false);
                characters.Add(actor);
            }

            initialized = true;
            CharacterActor initial = Find(initialCharacterId) ?? (characters.Count > 0 ? characters[0] : null);
            if (initial != null) SwitchInternal(initial);
        }

        /// <summary>按角色标识查找已通过初始化校验的角色。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <returns>找到时返回角色，否则返回空。</returns>
        public CharacterActor Find(CharacterId characterId)
        {
            foreach (CharacterActor actor in characters)
                if (actor.CharacterId == characterId) return actor;
            return null;
        }

        /// <summary>按初始化后的稳定队伍顺序读取指定槽位角色。</summary>
        /// <param name="slotIndex">从零开始的队伍槽位下标。</param>
        /// <returns>槽位存在时返回角色，否则返回空。</returns>
        public CharacterActor GetCharacterAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= characters.Count) return null;
            return characters[slotIndex];
        }

        /// <summary>只依据队伍内部状态尝试切换到指定槽位。</summary>
        /// <param name="slotIndex">从零开始的队伍槽位下标。</param>
        /// <returns>队伍切换结果；玩家级锁定由 PlayerController 判断。</returns>
        public CharacterSwitchStatus TrySwitchSlot(int slotIndex)
        {
            if (!initialized) return CharacterSwitchStatus.NotInitialized;
            CharacterActor target = GetCharacterAtSlot(slotIndex);
            if (target == null) return CharacterSwitchStatus.CharacterNotFound;
            return TrySwitch(target.CharacterId);
        }

        /// <summary>只根据队伍内部状态尝试切换角色。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>明确的队伍切换结果。</returns>
        public CharacterSwitchStatus TrySwitch(CharacterId characterId)
        {
            if (!initialized) return CharacterSwitchStatus.NotInitialized;
            CharacterActor target = Find(characterId);
            if (target == null) return CharacterSwitchStatus.CharacterNotFound;
            if (ReferenceEquals(target, ActiveCharacter)) return CharacterSwitchStatus.AlreadyActive;
            if (target.IsBusy || ActiveCharacter != null && ActiveCharacter.IsBusy) return CharacterSwitchStatus.CharacterBusy;
            SwitchInternal(target);
            return CharacterSwitchStatus.Success;
        }

        #endregion

        #region 阶段推进
        /// <summary>推进全部角色 ASC 的普通阶段，使后台冷却和持续效果继续计时。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickCharacters(float deltaTime)
        {
            // CharacterManager 只遍历队伍并转发阶段，不能由自身 Unity 生命周期自动调用。
            foreach (CharacterActor actor in characters) actor.TickAbility(deltaTime);
        }

        /// <summary>推进当前角色的输入消费和 Locomotion 普通阶段。</summary>
        /// <param name="inputRequests">提供技能 Request 的输入缓冲区。</param>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickActiveCharacter(IPlayerInputRequestBuffer inputRequests, float deltaTime)
        {
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            CharacterActor active = ActiveCharacter ??
                                    throw new InvalidOperationException("[CharacterManager] 尚未选出 ActiveCharacter。 ");
            // 切换可能刚在本帧完成，必须重新读取 ActiveCharacter，避免旧角色继续消费输入。
            active.ProcessAbilityInputRequests(inputRequests);
            active.Locomotion.Tick(deltaTime);
        }

        /// <summary>推进当前角色 ASC 与 Locomotion 的物理阶段，不执行最终移动。</summary>
        /// <param name="fixedDeltaTime">本物理步时长。</param>
        internal void FixedTickActiveCharacter(float fixedDeltaTime)
        {
            CharacterActor active = ActiveCharacter ??
                                    throw new InvalidOperationException("[CharacterManager] 物理阶段没有 ActiveCharacter。 ");
            // MotionDriver 的 Resolve 由 PlayerController 在本方法返回后执行，保证结算出口唯一。
            active.FixedTickAbility(fixedDeltaTime);
            active.Locomotion.FixedTick(fixedDeltaTime);
        }

        /// <summary>推进全队 ASC 与当前 Locomotion 的延迟阶段。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTickCharacters(float deltaTime)
        {
            // 后台角色仍推进 ASC 延迟阶段，只有当前角色推进 Locomotion 表现阶段。
            foreach (CharacterActor actor in characters) actor.LateTickAbility(deltaTime);
            CharacterActor active = ActiveCharacter ??
                                    throw new InvalidOperationException("[CharacterManager] 延迟阶段没有 ActiveCharacter。 ");
            active.Locomotion.LateTick(deltaTime);
        }

        #endregion

        #region 输入与动画阶段
        /// <summary>处理固定槽位输入并执行队伍内部的切换结果消费。</summary>
        /// <param name="inputRequests">提供 CharacterSlot1-4 Request 的输入缓冲区。</param>
        internal void ProcessSwitchInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            if (!initialized)
                throw new InvalidOperationException("[CharacterManager] 尚未初始化，不能处理角色切换输入。 ");

            for (int slotIndex = 0; slotIndex < characterSlotInputTypes.Length; slotIndex++)
            {
                if (!inputRequests.TryGetRequest(
                        characterSlotInputTypes[slotIndex],
                        out IReadOnlyPlayerInputRequest request) ||
                    !request.HasBufferedPress)
                {
                    continue;
                }

                CharacterSwitchStatus status = TrySwitchSlot(slotIndex);
                if (status == CharacterSwitchStatus.NotInitialized)
                    throw new InvalidOperationException("[CharacterManager] 切换输入处理时初始化状态失效。 ");

                // Busy 需要保留 Press，其他终态已经完成识别，直接确认当前 Request。
                if (status != CharacterSwitchStatus.CharacterBusy)
                    inputRequests.TryConfirmConsumed(request.PressHandle);
                return;
            }
        }

        /// <summary>推进当前角色 Animator 求值阶段，不执行 MotionDriver 最终结算。</summary>
        /// <param name="source">产生 AnimatorMove 的角色。</param>
        /// <returns>来源是当前角色并已推进时返回 true。</returns>
        internal bool TryUpdateAnimationMove(CharacterActor source, Vector3 deltaPosition, Quaternion deltaRotation,
            float evaluationDeltaTime)
        {
            if (!ReferenceEquals(source, ActiveCharacter)) return false;
            // Animator 阶段只推进角色业务；根位移由 PlayerController 保存并交给 MotionDriver。
            source.UpdateAnimationMoveAbility(deltaPosition, deltaRotation);
            source.Locomotion.UpdateAnimationMove(deltaPosition, deltaRotation, evaluationDeltaTime);
            return true;
        }

        #endregion

        #region 内部切换
        /// <summary>完成表现停用、当前引用更新与同步事件发送。</summary>
        /// <param name="target">已经通过队伍校验的目标角色。</param>
        private void SwitchInternal(CharacterActor target)
        {
            CharacterActor previous = ActiveCharacter;
            previous?.SetActivePresentation(false);
            ActiveCharacter = target;
            target.SetActivePresentation(true);
            ActiveCharacterChanged?.Invoke(previous, target);
        }

        #endregion
    }
}
