using System;
using System.Collections.Generic;
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

        #endregion

        #region 事件与属性

        /// <summary>在 ActiveCharacter 完成同步切换后发送一次。</summary>
        public event Action<CharacterActor, CharacterActor> ActiveCharacterChanged;
        /// <summary>获取全部通过 Marker 校验的队伍角色。</summary>
        public IReadOnlyList<CharacterActor> Characters => characters;
        /// <summary>获取当前由玩家操控的角色。</summary>
        public CharacterActor ActiveCharacter { get; private set; }

        #endregion

        #region 初始化与队伍操作

        /// <summary>构建常驻队伍并校验角色身份与 Marker；不接收运动驱动。</summary>
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

        /// <summary>推进全部角色 ASC 的普通阶段，使后台冷却和持续效果继续计时。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickCharacters(float deltaTime)
        {
            foreach (CharacterActor actor in characters) actor.TickAbility(deltaTime);
        }

        /// <summary>推进全部角色 ASC 的延迟阶段。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTickCharacters(float deltaTime)
        {
            foreach (CharacterActor actor in characters) actor.LateTickAbility(deltaTime);
        }

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
