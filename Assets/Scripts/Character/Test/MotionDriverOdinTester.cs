#if UNITY_EDITOR
using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>通过 Inspector 检查当前角色 MotionDriver 的最终移动许可。</summary>
    public sealed class MotionDriverOdinTester : MonoBehaviour
    {
        [SerializeField, Required] private PlayerController playerController;

        /// <summary>输出 Active ASC Tag 约束后的水平移动许可。</summary>
        [Button]
        private void LogHorizontalPermission() => Debug.Log(
            $"[MotionDriverTester] CanMoveHorizontally={playerController.MotionDriver.CanMoveHorizontally}", this);

        /// <summary>使用临时碰撞体验证真实驱动的优先级、通道拆分、恢复与重复释放。</summary>
        [Button("验证运动仲裁"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void VerifyArbitration()
        {
            CharacterActor owner = playerController.CharacterManager.ActiveCharacter;
            GameObject probe = new GameObject("MotionDriver verification probe");
            probe.transform.position = new Vector3(0, 10000, 0);
            CharacterController controller = probe.AddComponent<CharacterController>();
            controller.minMoveDistance = 0;
            var driver = new MotionDriver();
            driver.Initialize(controller);
            driver.SetActiveOwner(owner, owner.AbilitySystemComponent);
            try
            {
                using MotionControlHandle walk = driver.RequestControl(new MotionControlRequest(owner,
                    MotionPriority.Locomotion, MotionChannels.Horizontal, false));
                using MotionControlHandle gravity = driver.RequestControl(new MotionControlRequest(owner,
                    MotionPriority.Gravity, MotionChannels.Vertical, false));
                MotionControlHandle skill = driver.RequestControl(new MotionControlRequest(owner,
                    MotionPriority.Skill, MotionChannels.Horizontal, false));
                Vector3 before = probe.transform.position;
                driver.SubmitFixed(walk, FixedMotionRequest.TranslationOnly(Vector3.right * 10));
                driver.SubmitFixed(skill, FixedMotionRequest.TranslationOnly(Vector3.right));
                driver.SubmitFixed(skill, FixedMotionRequest.TranslationOnly(Vector3.right));
                driver.SubmitFixed(gravity, FixedMotionRequest.TranslationOnly(Vector3.down));
                driver.ResolveFixedMotion();
                ExpectDelta(probe, before, new Vector3(2, -1, 0), "技能覆盖、同句柄求和、垂直通道独立");
                skill.Dispose();
                skill.Dispose();
                before = probe.transform.position;
                driver.SubmitFixed(walk, FixedMotionRequest.TranslationOnly(Vector3.right));
                driver.ResolveFixedMotion();
                ExpectDelta(probe, before, Vector3.right, "释放技能后 Locomotion 恢复");
                before = probe.transform.position;
                driver.ResolveFixedMotion();
                ExpectDelta(probe, before, Vector3.zero, "瞬时提交不跨步复用");
                using MotionControlHandle newer = driver.RequestControl(new MotionControlRequest(owner,
                    MotionPriority.Locomotion, MotionChannels.Horizontal, false));
                before = probe.transform.position;
                driver.SubmitFixed(walk, FixedMotionRequest.TranslationOnly(Vector3.right));
                driver.ResolveAnimatorMotion(Vector3.forward, Quaternion.identity);
                driver.ResolveFixedMotion();
                ExpectDelta(probe, before, Vector3.zero, "同优先级后建立站桩请求阻断代码与根运动");
                Debug.Log("[MotionDriverTester] 全部仲裁检查通过。", this);
            }
            finally
            {
                // 临时对象只在按钮执行期间存在，禁止污染测试场景和 Player 的真实控制请求。
                driver.ReleaseAll(owner);
                DestroyImmediate(probe);
            }
        }

        /// <summary>检查临时角色本步世界位移，失败立即暴露。</summary>
        /// <param name="probe">独立测试对象。</param>
        /// <param name="before">移动前世界位置。</param>
        /// <param name="expected">预期世界位移。</param>
        /// <param name="label">检查说明。</param>
        private static void ExpectDelta(GameObject probe, Vector3 before, Vector3 expected, string label)
        {
            Vector3 actual = probe.transform.position - before;
            if ((actual - expected).sqrMagnitude > 0.0001f)
                throw new InvalidOperationException($"[MotionDriverTester] {label}：预期 {expected}，实际 {actual}。");
        }
    }
}
#endif
