using System;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.Character
{
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

            SerializedProperty rarity = RequireProperty(serializedObject, "rarity");
            SerializedProperty sideIconAddress = RequireProperty(serializedObject, "sideIconAddress");
            SerializedProperty avatarAddress = RequireProperty(serializedObject, "avatarAddress");
            SerializedProperty gravity = RequireProperty(serializedObject, "gravity");
            SerializedProperty maxLevel = RequireProperty(serializedObject, "maxLevel");
            SerializedProperty maxAscensionRank = RequireProperty(serializedObject, "maxAscensionRank");
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
