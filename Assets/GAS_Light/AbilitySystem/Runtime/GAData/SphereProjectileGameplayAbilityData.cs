using System;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>基础球体投射物 Ability 示例；创建对象后由投射物自身继续运行。</summary>
    [CreateAssetMenu(fileName = "SphereProjectileGameplayAbility", menuName = "WSFrame/GAS/Gameplay Ability/Projectile/Sphere Test")]
    public sealed class SphereProjectileGameplayAbilityData : ProjectileGameplayAbilityData
    {
        #region 字段与属性
        [SerializeField, Min(0.01f), Tooltip("生成球体的半径。")]
        private float radius = 0.25f;
        [SerializeField, Min(0f), Tooltip("球体前进速度；仅用于编辑器测试。")]
        private float speed = 5f;
        [SerializeField, Min(0f), Tooltip("球体独立存活时间；仅用于编辑器测试。")]
        private float lifetime = 2f;
        [NonSerialized] private Transform spawnTransform;
        [NonSerialized] private GameObject spawnedObject;

        /// <summary>获取本次测试创建的球体对象。</summary>
        internal GameObject SpawnedObject => spawnedObject;
        #endregion

        #region 测试入口
        // 测试组件提供场景出生点，Transform 不写入 Ability SO。
        internal void Initialize(Transform value) => spawnTransform = value;
        #endregion

        #region 同步执行
        // 创建对象后立即返回；投射物的移动与销毁不延长 GA Runtime。
        protected override void SpawnProjectile(SynchronousGameplayAbilityRuntime runtime)
        {
            Vector3 position = spawnTransform != null ? spawnTransform.position : Vector3.zero;
            Vector3 direction = spawnTransform != null ? spawnTransform.forward : Vector3.forward;
            spawnedObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spawnedObject.name = "GA Sphere Projectile (Test)";
            spawnedObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            spawnedObject.transform.localScale = Vector3.one * (radius * 2f);
            SphereProjectileProbe probe = spawnedObject.AddComponent<SphereProjectileProbe>();
            probe.Initialize(direction, speed, lifetime);
        }
        #endregion

        #region 测试投射物
        // 仅模拟投射物持续更新，不处理碰撞、范围、阵营或目标选择。
        private sealed class SphereProjectileProbe : MonoBehaviour
        {
            private Vector3 direction;
            private float speed;
            private float remainingLife;

            // 初始化独立投射物的移动参数。
            internal void Initialize(Vector3 value, float moveSpeed, float life)
            {
                direction = value.sqrMagnitude > 0f ? value.normalized : Vector3.forward;
                speed = moveSpeed;
                remainingLife = life;
            }

            // 投射物在自身生命周期内移动，到期后自行销毁。
            private void Update()
            {
                transform.position += direction * (speed * Time.deltaTime);
                remainingLife -= Time.deltaTime;
                if (remainingLife <= 0f) Destroy(gameObject);
            }
        }
        #endregion
    }
}
