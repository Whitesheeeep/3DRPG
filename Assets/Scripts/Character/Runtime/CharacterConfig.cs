using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>保存角色运行时所需的身份、Prefab 地址、属性模板与移动配置。</summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "RPG/Character/Character Config")]
    public sealed class CharacterConfig : ScriptableObject
    {
        #region 配置字段
        [SerializeField, ReadOnly, LabelText("角色标识")]
        private CharacterId characterId;
        [SerializeField, LabelText("角色名称")]
        private string characterName;
        [SerializeField, LabelText("稀有度")]
        private CharacterRarity rarity = CharacterRarity.Five;
        [SerializeField, WSAddressableKey("CharacterPrefabs"), LabelText("角色 Prefab 地址")]
        private string prefabAddress;
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("侧面头像图集 Address")]
        private string sideIconAddress = CharacterAssetAddresses.SideIconsAtlas;
        [SerializeField, LabelText("侧面头像 Sprite 名称")]
        private string sideIconSpriteName;
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("角色头像图集 Address")]
        private string avatarAddress = CharacterAssetAddresses.AvatarAtlas;
        [SerializeField, LabelText("角色头像 Sprite 名称")]
        private string avatarSpriteName;
        [SerializeField, LabelText("初始属性集")]
        private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField, LabelText("能力输入绑定")]
        private CharacterAbilityInputBinding[] abilityInputBindings = Array.Empty<CharacterAbilityInputBinding>();
        [SerializeField, MinValue(0f), LabelText("重力")]
        private float gravity = 9.81f;
        [SerializeField, Required, LabelText("Locomotion 状态过渡")]
        private PlayerFSMTransition locomotionTransition;

#if UNITY_EDITOR
        [SerializeField, HideInInspector]
        private Sprite editorSideIcon;
        [SerializeField, HideInInspector]
        private Sprite editorAvatar;
#endif
        #endregion

        #region 属性
        /// <summary>获取稳定角色标识。</summary>
        public CharacterId CharacterId => characterId;
        /// <summary>获取用于编辑器和界面展示的角色名称。</summary>
        public string Name => characterName;
        /// <summary>获取角色稀有度。</summary>
        public CharacterRarity Rarity => rarity;
        /// <summary>获取 Addressables 角色 Prefab 地址。</summary>
        public string PrefabAddress => prefabAddress;
        /// <summary>获取侧面头像 SpriteAtlas 的 Addressable Address。</summary>
        public string SideIconAddress => sideIconAddress;
        /// <summary>获取侧面头像图集内的 Sprite 名称。</summary>
        public string SideIconSpriteName => sideIconSpriteName;
        /// <summary>获取角色头像 SpriteAtlas 的 Addressable Address。</summary>
        public string AvatarAddress => avatarAddress;
        /// <summary>获取角色头像图集内的 Sprite 名称。</summary>
        public string AvatarSpriteName => avatarSpriteName;
        /// <summary>获取角色初始属性集，顺序保持作者配置。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets => initialAttributeSets;
        /// <summary>获取角色能力输入绑定，顺序保持作者配置。</summary>
        public IReadOnlyList<CharacterAbilityInputBinding> AbilityInputBindings => abilityInputBindings;
        /// <summary>获取角色 Locomotion 重力。</summary>
        public float Gravity => gravity;
        /// <summary>获取角色 Locomotion 状态过渡配置。</summary>
        public PlayerFSMTransition LocomotionTransition => locomotionTransition;

#if UNITY_EDITOR
        /// <summary>获取 Editor 预览用侧面头像。</summary>
        public Sprite EditorSideIcon => editorSideIcon;
        /// <summary>获取 Editor 预览用角色头像。</summary>
        public Sprite EditorAvatar => editorAvatar;
#endif
        #endregion

        #region 校验
        /// <summary>校验角色配置是否可被 CharacterManager 加载。</summary>
        /// <exception cref="InvalidOperationException">配置缺少必需字段时抛出。</exception>
        public void Validate()
        {
            if (!characterId.IsValid)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 CharacterId 无效。");
            if (string.IsNullOrWhiteSpace(characterName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的角色名称不能为空。");
            if (!Enum.IsDefined(typeof(CharacterRarity), rarity))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Rarity 无效。");
            if (string.IsNullOrWhiteSpace(prefabAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 PrefabAddress。");
            if (string.IsNullOrWhiteSpace(sideIconAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconAddress。");
            if (string.IsNullOrWhiteSpace(sideIconSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconSpriteName。");
            if (string.IsNullOrWhiteSpace(avatarAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarAddress。");
            if (string.IsNullOrWhiteSpace(avatarSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarSpriteName。");
            if (float.IsNaN(gravity) || float.IsInfinity(gravity) || gravity < 0f)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Gravity 必须是非负有限值。");
            if (locomotionTransition == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 LocomotionTransition。");
            ValidateList(initialAttributeSets, "InitialAttributeSets");
            ValidateList(abilityInputBindings, "AbilityInputBindings");
        }

        /// <summary>校验配置列表中不存在空元素。</summary>
        /// <typeparam name="T">列表元素类型。</typeparam>
        /// <param name="items">待校验列表。</param>
        /// <param name="fieldName">字段名称。</param>
        private void ValidateList<T>(IReadOnlyList<T> items, string fieldName) where T : class
        {
            if (items == null) throw new InvalidOperationException($"CharacterConfig '{name}' 的 {fieldName} 为空。");
            for (int index = 0; index < items.Count; index++)
                if (items[index] == null)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的 {fieldName}[{index}] 为空。");
        }
        #endregion
    }
}