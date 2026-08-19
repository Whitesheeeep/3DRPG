using RPG.Character;
using RPG.Markers;
using RPG.SkillSystem;
using UnityEngine;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>为纯 GAS 测试对象提供最小 ASC Owner 宿主，不引入角色输入或 SkillSystem 依赖。</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayAbilitySystemTestOwner : MonoBehaviour, IGameplayAbilitySystemOwner
    {
        private IMarkerProvider markerProvider;
        private ISkillRuntimeHost skillRuntimeHost;
        private IMotionDriver motionDriver;

        /// <inheritdoc />
        public Transform RootTransform => transform;

        /// <inheritdoc />
        public IMarkerProvider MarkerProvider => markerProvider;
        public ISkillRuntimeHost SkillRuntimeHost { get; }
        public IMotionDriver MotionDriver { get; }

        /// <summary>缓存测试对象根节点的 Marker Provider。</summary>
        private void Awake()
        {
            markerProvider = GetComponent<IMarkerProvider>();
            skillRuntimeHost = GetComponent<ISkillRuntimeHost>();
            motionDriver = GetComponent<IMotionDriver>();
        }
    }
}
