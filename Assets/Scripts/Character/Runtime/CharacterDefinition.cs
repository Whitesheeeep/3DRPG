using UnityEngine;

namespace RPG.Character
{
    /// <summary>定义一个可加入玩家队伍的角色包装 Prefab。</summary>
    [CreateAssetMenu(menuName = "RPG/Character/Character Definition")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [SerializeField] private CharacterId characterId;
        [SerializeField] private CharacterActor actorPrefab;

        /// <summary>获取角色稳定标识。</summary>
        public CharacterId CharacterId => characterId;

        /// <summary>获取要实例化到 CharacterRoot 下的角色包装 Prefab。</summary>
        public CharacterActor ActorPrefab => actorPrefab;
    }
}
