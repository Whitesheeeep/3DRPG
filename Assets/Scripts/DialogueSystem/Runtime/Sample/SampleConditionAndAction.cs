using UnityEngine;

namespace RPG.DialogueSystemModule.Test
{
    public class SampleDialogueCondition: DialogueCondition
    {
        [SerializeField] private int money = 100;

        public override DialogueConditionResult Evaluate(DialogueCommandContext context)
        {
            Debug.Log($"Evaluating condition: money >= {money}");
            return new DialogueConditionResult(money >= 100, $"Money is {money}, required: {100}");
        }
    }

    public class SampleDialogueAction: DialogueAction
    {
        [SerializeField] private int moneyChange = -10;

        public override void Execute(DialogueCommandContext context)
        {
            Debug.Log($"Executing action: change money by {moneyChange}");
            // Here you would implement the logic to change the player's money.
        }
    }
}