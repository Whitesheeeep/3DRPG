using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.Character
{
    /// <summary>描述一个武器类型对应的角色默认武器 Definition。</summary>
    [Serializable]
    public sealed class CharacterDefaultWeaponData
    {
        [SerializeField, LabelText("武器类型")] private WeaponType weaponType = WeaponType.Sword;
        [SerializeField, LabelText("默认武器 Definition"), ItemIdDropdown] private ItemId weaponDefinitionId = new ItemId("weapon_0003");

        /// <summary>获取该映射覆盖的武器类型。</summary>
        public WeaponType WeaponType => weaponType;

        /// <summary>获取该类型使用的默认武器 Definition 标识。</summary>
        public ItemId WeaponDefinitionId => weaponDefinitionId;

        /// <summary>校验默认武器映射本身的稳定字段。</summary>
        /// <param name="index">该映射在数据库列表中的下标。</param>
        /// <exception cref="InvalidOperationException">映射包含非法类型或 Definition 标识时抛出。</exception>
        internal void Validate(int index)
        {
            if (!Enum.IsDefined(typeof(WeaponType), weaponType))
                throw new InvalidOperationException($"角色默认武器映射第 {index} 项的武器类型无效。");
            if (!weaponDefinitionId.IsValid)
                throw new InvalidOperationException($"角色默认武器映射第 {index} 项缺少武器 Definition 标识。");
        }
    }

    /// <summary>角色数据库复用的稳定通用默认字段。</summary>
    [Serializable]
    public sealed class CharacterDefaultData
    {
        #region 默认字段

        [SerializeField, LabelText("默认稀有度")] private CharacterRarity defaultRarity = CharacterRarity.Five;
        [SerializeField, LabelText("默认侧面头像图集 Address")] private string defaultSideIconAddress = CharacterAssetAddresses.SideIconsAtlas;
        [SerializeField, LabelText("默认角色头像图集 Address")] private string defaultAvatarAddress = CharacterAssetAddresses.AvatarAtlas;
        [SerializeField, MinValue(0f), LabelText("默认重力")] private float defaultGravity = 9.81f;
        [SerializeField, MinValue(1), LabelText("默认最大等级")] private int defaultMaxLevel = 90;
        [SerializeField, MinValue(0), LabelText("默认最大突破阶数")] private int defaultMaxAscensionRank = 6;
        [SerializeField, LabelText("武器类型默认 Definition")] private List<CharacterDefaultWeaponData> defaultWeapons = new();

        #endregion

        #region 属性

        /// <summary>获取默认稀有度。</summary>
        public CharacterRarity DefaultRarity => defaultRarity;

        /// <summary>获取默认侧面头像图集 Address。</summary>
        public string DefaultSideIconAddress => defaultSideIconAddress;

        /// <summary>获取默认角色头像图集 Address。</summary>
        public string DefaultAvatarAddress => defaultAvatarAddress;

        /// <summary>获取默认重力。</summary>
        public float DefaultGravity => defaultGravity;

        /// <summary>获取默认最大等级。</summary>
        public int DefaultMaxLevel => defaultMaxLevel;

        /// <summary>获取默认最大突破阶数。</summary>
        public int DefaultMaxAscensionRank => defaultMaxAscensionRank;

        /// <summary>获取武器类型到默认武器 Definition 的配置列表。</summary>
        public IReadOnlyList<CharacterDefaultWeaponData> DefaultWeapons => defaultWeapons;

        #endregion

        #region 校验

        /// <summary>验证角色默认字段。</summary>
        /// <exception cref="InvalidOperationException">默认数据不满足配置契约时抛出。</exception>
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(CharacterRarity), defaultRarity))
                throw new InvalidOperationException("角色默认稀有度无效。");
            if (string.IsNullOrWhiteSpace(defaultSideIconAddress))
                throw new InvalidOperationException("角色默认侧面头像图集 Address 不能为空。");
            if (string.IsNullOrWhiteSpace(defaultAvatarAddress))
                throw new InvalidOperationException("角色默认头像图集 Address 不能为空。");
            if (float.IsNaN(defaultGravity) || float.IsInfinity(defaultGravity) || defaultGravity < 0f)
                throw new InvalidOperationException("角色默认重力必须是非负有限值。");
            if (defaultMaxLevel < 1) throw new InvalidOperationException("角色默认最大等级必须大于零。");
            if (defaultMaxAscensionRank < 0) throw new InvalidOperationException("角色默认最大突破阶数不能为负数。");
            if (defaultWeapons == null || defaultWeapons.Count == 0)
                throw new InvalidOperationException("角色默认武器映射不能为空。");

            var configuredWeaponTypes = new HashSet<WeaponType>();
            for (int index = 0; index < defaultWeapons.Count; index++)
            {
                CharacterDefaultWeaponData mapping = defaultWeapons[index];
                if (mapping == null)
                    throw new InvalidOperationException($"角色默认武器映射第 {index} 项为空。");
                mapping.Validate(index);
                if (!configuredWeaponTypes.Add(mapping.WeaponType))
                    throw new InvalidOperationException($"角色默认武器映射重复配置武器类型：{mapping.WeaponType}。");
            }
        }

        /// <summary>按武器类型尝试获取默认武器 Definition。</summary>
        /// <param name="weaponType">待查询的武器类型。</param>
        /// <param name="definitionId">找到的默认武器 Definition 标识。</param>
        /// <returns>存在对应映射时返回 true。</returns>
        public bool TryGetDefaultWeaponDefinitionId(WeaponType weaponType, out ItemId definitionId)
        {
            for (int index = 0; index < defaultWeapons.Count; index++)
            {
                CharacterDefaultWeaponData mapping = defaultWeapons[index];
                if (mapping != null && mapping.WeaponType == weaponType)
                {
                    definitionId = mapping.WeaponDefinitionId;
                    return true;
                }
            }

            definitionId = default;
            return false;
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器默认值应用

        /// <summary>将稳定通用默认字段写入 CharacterConfig，不修改角色专属成长内容。</summary>
        /// <param name="serializedObject">目标 CharacterConfig 的序列化对象。</param>
        /// <exception cref="ArgumentNullException">序列化对象为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">目标对象或字段不满足默认数据契约时抛出。</exception>
        public void ApplyDefault(SerializedObject serializedObject)
        {
            Validate();
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            if (!(serializedObject.targetObject is CharacterConfig config))
                throw new InvalidOperationException($"角色默认数据要求目标为 CharacterConfig，实际为 {serializedObject.targetObject.GetType().Name}。");

            SerializedProperty rarity = RequireProperty(serializedObject, "identity.rarity");
            SerializedProperty sideIconAddress = RequireProperty(serializedObject, "presentation.sideIconAddress");
            SerializedProperty avatarAddress = RequireProperty(serializedObject, "presentation.avatarAddress");
            SerializedProperty gravity = RequireProperty(serializedObject, "locomotion.gravity");
            SerializedProperty maxLevel = RequireProperty(serializedObject, "progression.maxLevel");
            SerializedProperty maxAscensionRank = RequireProperty(serializedObject, "progression.maxAscensionRank");
            rarity.intValue = (int)DefaultRarity;
            sideIconAddress.stringValue = DefaultSideIconAddress;
            avatarAddress.stringValue = DefaultAvatarAddress;
            gravity.floatValue = DefaultGravity;
            maxLevel.intValue = DefaultMaxLevel;
            maxAscensionRank.intValue = DefaultMaxAscensionRank;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
        }

        /// <summary>取得必须存在的角色序列化字段。</summary>
        /// <param name="serializedObject">目标序列化对象。</param>
        /// <param name="propertyName">字段名。</param>
        /// <returns>找到的字段。</returns>
        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"{serializedObject.targetObject.name} 缺少序列化字段 '{propertyName}'。");
            return property;
        }

        #endregion
#endif
    }
}
