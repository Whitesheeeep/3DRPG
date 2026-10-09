#if UNITY_EDITOR
using RPG.Game;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;

namespace RPG.NPC
{
    /// <summary>通过真实 ASC Health 变化验证 NPC 死亡状态与 Odetta 击败任务。</summary>
    [InfoBox("仅用于 Editor Play Mode：先接受 Odetta 击败任务，再将场景 NPC Health 设为 0。死亡动画结束后该 NPC 会销毁。")]
    public sealed class NPCDefeatTaskOdinTester : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private NPCController targetNPC;

        #endregion

        #region 查询

        /// <summary>显示目标的死亡事实、状态机叶状态和当前 Health。</summary>
        [ShowInInspector, ReadOnly, LabelText("目标状态")]
        private string TargetStatus
        {
            get
            {
                if (targetNPC == null || !targetNPC.IsInitialized)
                    return "未绑定或尚未初始化";
                targetNPC.Actor.AbilitySystemComponent.TryGetCurrentValue(
                    GameplayAttributes.Attribute_Health,
                    out float health);
                return $"Health={health:F1}, IsDead={targetNPC.IsDead}, State={targetNPC.Locomotion.CurrentState}";
            }
        }

        #endregion

        #region 手动验证

        /// <summary>通过 TaskSystem 正式接取 Odetta 击败任务。</summary>
        [Button("接取 Odetta 击败任务")]
        public void AcceptOdettaDefeatTask()
        {
            TaskAcceptResult result = GameArchitecture.Interface.GetSystem<TaskSystem>()
                .TryAcceptTask(new TaskId("side_defeat_boss_odetta"), E_TaskAcceptSource.Test);
            Debug.Log($"[NPCDefeatTaskOdinTester] Odetta 击败任务接取结果：{result}。");
        }

        /// <summary>通过 ASC Resource API 将目标 Health 设为零，驱动完整死亡 Transition。</summary>
        [Button("触发致命伤害")]
        public void SetTargetHealthToZero()
        {
            bool applied = targetNPC.Actor.AbilitySystemComponent.TrySetResourceCurrentValues(
                new[] { new GameplayAttributeValue(GameplayAttributes.Attribute_Health, 0f) });
            Debug.Log(
                $"[NPCDefeatTaskOdinTester] 请求致命伤害，applied={applied}, npc={targetNPC.name}。",
                targetNPC);
        }

        #endregion
    }
}
#endif
