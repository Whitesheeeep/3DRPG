#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Game.JumpText.Tests
{
    /// <summary>
    /// 通过 Odin 按钮验证跳字格式、样式、容量、随机轨迹和 GPU Instancing 批次。
    /// </summary>
    public sealed class JumpTextOdinTester : MonoBehaviour
    {
        #region 测试输入与依赖字段

        [SerializeField, Required, Tooltip("场景中的 JumpTextManager；通常来自 WSFrameRoot。")]
        private JumpTextManager jumpTextManager;
        [SerializeField, Tooltip("为空时使用 Fallback Position 作为出生点。")]
        private Transform worldAnchor;
        [SerializeField, LabelText("备用世界坐标")]
        private Vector3 fallbackPosition;
        [SerializeField, MinValue(0), LabelText("测试数值")]
        private int testValue = 1234;
        [SerializeField, MinValue(1), LabelText("爆发条数")]
        private int burstCount = 1500;

        #endregion

        #region 单条样式测试

        /// <summary>在测试锚点显示普通伤害样式。</summary>
        [Button("显示普通伤害", ButtonSizes.Medium)]
        private void ShowNormalDamage()
        {
            jumpTextManager.Show(testValue, ResolvePosition(), JumpTextStyle.NormalDamage);
            Debug.Log($"[JumpTextOdinTester] 普通伤害已提交，value={testValue}。", this);
        }

        /// <summary>在测试锚点显示暴击伤害样式。</summary>
        [Button("显示暴击伤害", ButtonSizes.Medium)]
        private void ShowCriticalDamage()
        {
            jumpTextManager.Show(testValue, ResolvePosition(), JumpTextStyle.CriticalDamage);
            Debug.Log($"[JumpTextOdinTester] 暴击伤害已提交，value={testValue}。", this);
        }

        /// <summary>在测试锚点显示带加号的治疗样式。</summary>
        [Button("显示治疗", ButtonSizes.Medium)]
        private void ShowHealing()
        {
            jumpTextManager.Show(testValue, ResolvePosition(), JumpTextStyle.Healing, true);
            Debug.Log($"[JumpTextOdinTester] 治疗已提交，value=+{testValue}。", this);
        }

        /// <summary>验证负数减号和零值的字符展开。</summary>
        [Button("测试符号与零", ButtonSizes.Medium)]
        private void ShowSignsAndZero()
        {
            Vector3 position = ResolvePosition();
            jumpTextManager.Show(-testValue, position + Vector3.left * 0.25f, JumpTextStyle.NormalDamage);
            // 即便请求打开正号，零仍按规则只显示一个 "0"。
            jumpTextManager.Show(0, position, JumpTextStyle.NormalDamage, true);
            jumpTextManager.Show(testValue, position + Vector3.right * 0.25f,
                JumpTextStyle.Healing, true);
            Debug.Log("[JumpTextOdinTester] 已提交负数、零值和正号样例。", this);
        }

        /// <summary>验证 int 边界值不会因绝对值溢出而失败。</summary>
        [Button("测试 int 边界", ButtonSizes.Medium)]
        private void ShowIntegerBoundaries()
        {
            Vector3 position = ResolvePosition();
            jumpTextManager.Show(int.MinValue, position + Vector3.left * 0.5f,
                JumpTextStyle.CriticalDamage);
            jumpTextManager.Show(int.MaxValue, position + Vector3.right * 0.5f,
                JumpTextStyle.CriticalDamage);
            Debug.Log("[JumpTextOdinTester] 已提交 int.MinValue 和 int.MaxValue。", this);
        }

        /// <summary>使用相同随机种子提交两条轨迹，验证横向扰动可复现。</summary>
        [Button("测试确定性随机", ButtonSizes.Medium)]
        private void ShowDeterministicRandom()
        {
            Vector3 position = ResolvePosition();
            JumpTextRequest left = new(testValue, position + Vector3.left * 0.35f,
                JumpTextStyle.NormalDamage, false, 4242);
            JumpTextRequest right = new(testValue, position + Vector3.right * 0.35f,
                JumpTextStyle.NormalDamage, false, 4242);
            jumpTextManager.Show(in left);
            jumpTextManager.Show(in right);
            Debug.Log("[JumpTextOdinTester] 已用 seed=4242 提交两条确定性轨迹。", this);
        }

        #endregion

        #region 容量与生命周期测试

        /// <summary>连续提交大量短数字，观察 1023 分批和容量淘汰。</summary>
        [Button("爆发提交", ButtonSizes.Large)]
        private void SubmitBurst()
        {
            Vector3 origin = ResolvePosition();
            for (int index = 0; index < burstCount; index++)
            {
                Vector3 offset = new(
                    (index % 20 - 10) * 0.04f,
                    (index / 20 % 10) * 0.04f,
                    (index / 200) * 0.02f);
                jumpTextManager.Show(index % 2 == 0 ? testValue : -testValue,
                    origin + offset,
                    index % 3 == 0 ? JumpTextStyle.CriticalDamage : JumpTextStyle.NormalDamage);
            }

            Debug.Log($"[JumpTextOdinTester] 爆发提交完成，requested={burstCount}，activeTexts={jumpTextManager.ActiveTextCount}，activeGlyphs={jumpTextManager.ActiveGlyphCount}。", this);
        }

        /// <summary>清空活跃跳字并验证运行时状态归零。</summary>
        [Button("清空跳字", ButtonSizes.Medium)]
        private void ClearJumpTexts()
        {
            jumpTextManager.Clear();
            Debug.Log("[JumpTextOdinTester] 已调用 JumpTextManager.Clear。", this);
        }

        /// <summary>打印当前活跃条数、字符数和最近绘制批次数。</summary>
        [Button("打印统计", ButtonSizes.Medium)]
        private void LogStatistics()
        {
            Debug.Log(
                $"[JumpTextOdinTester] activeTexts={jumpTextManager.ActiveTextCount}，activeGlyphs={jumpTextManager.ActiveGlyphCount}，lastDrawCalls={jumpTextManager.LastDrawCallCount}。",
                this);
        }

        /// <summary>切换缩放时间暂停状态，验证动画是否随游戏暂停。</summary>
        /// <param name="paused">是否暂停游戏时间。</param>
        [Button("设置暂停")]
        private void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
            Debug.Log($"[JumpTextOdinTester] Time.timeScale={Time.timeScale}。", this);
        }

        #endregion

        #region 测试辅助

        /// <summary>解析显式 Transform 或备用世界坐标。</summary>
        /// <returns>跳字测试出生点。</returns>
        private Vector3 ResolvePosition() => worldAnchor != null ? worldAnchor.position : fallbackPosition;

        #endregion
    }
}
#endif
