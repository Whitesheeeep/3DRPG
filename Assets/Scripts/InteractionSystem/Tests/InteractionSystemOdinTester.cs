#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.InteractionSystem.Tests
{
    /// <summary>
    /// 通过 Odin Inspector 手动验证交互扫描、选项筛选、循环选择和命令执行入口。
    /// </summary>
    public sealed class InteractionSystemOdinTester : MonoBehaviour
    {
        #region 测试引用

        [Title("交互系统")]
        [SerializeField, Required] private PlayerInteractor interactor;

        [Title("物品交互")]
        [SerializeField] private ItemInteractable itemInteractable;

        #endregion

        #region 检测测试

        /// <summary>调用真实 Detector 执行一次胶囊扫描并输出 Provider 与 Option 状态。</summary>
        [Button("立即扫描")]
        public void ScanNow()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            target.Detector.ScanNow();
            LogState("立即扫描");
        }

        /// <summary>暂停真实交互检测，验证 Provider、Option 和选择状态被清空。</summary>
        [Button("暂停检测")]
        public void PauseDetect()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            target.PauseDetect();
            LogState("暂停检测");
        }

        /// <summary>恢复真实交互检测，验证恢复时立即重新扫描而不是等待重新进入范围。</summary>
        [Button("恢复检测")]
        public void StartDetect()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            target.StartDetect();
            LogState("恢复检测");
        }

        #endregion

        #region 选择与执行测试

        /// <summary>调用上一项循环选择 API 并输出选择是否发生变化。</summary>
        [Button("选择上一项")]
        public void SelectPrevious()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            bool changed = target.SelectPrevious();
            Debug.Log($"[InteractionTest] Previous changed={changed}", this);
            LogState("选择上一项");
        }

        /// <summary>调用下一项循环选择 API 并输出选择是否发生变化。</summary>
        [Button("选择下一项")]
        public void SelectNext()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            bool changed = target.SelectNext();
            Debug.Log($"[InteractionTest] Next changed={changed}", this);
            LogState("选择下一项");
        }

        /// <summary>调用真实选中 Option 的执行入口，验证执行前业务校验仍会再次运行。</summary>
        [Button("执行当前项")]
        public void SubmitSelected()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            bool succeeded = target.SubmitSelected();
            Debug.Log($"[InteractionTest] Execute succeeded={succeeded}", this);
            LogState("执行当前项");
        }

        /// <summary>按列表索引模拟 ChoiceWindowView 的点击流程，先选择再提交。</summary>
        /// <param name="index">待点击的 Option 索引。</param>
        [Button("按索引选择并执行")]
        public void SelectAndSubmitAtIndex(int index)
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;
            if (index < 0 || index >= target.Options.Count)
            {
                Debug.LogWarning($"[InteractionTest] Option 索引无效：{index}", this);
                return;
            }

            bool selected = target.Select(target.Options[index].Id);
            bool succeeded = selected && target.SubmitSelected();
            Debug.Log($"[InteractionTest] Click index={index}, selected={selected}, succeeded={succeeded}", this);
            LogState("按索引选择并执行");
        }

        #endregion

        #region 物品测试

        /// <summary>扫描并提交配置物品的拾取 Option，验证接收器成功后物品被停用。</summary>
        [Button("测试物品拾取")]
        public void SubmitItemPickup()
        {
            if (!TryGetInteractor(out PlayerInteractor target) || itemInteractable == null)
            {
                Debug.LogError("[InteractionTest] 请同时配置 PlayerInteractor 和 ItemInteractable。", this);
                return;
            }

            target.Detector.ScanNow();
            for (int index = 0; index < target.Options.Count; index++)
            {
                InteractionOption option = target.Options[index];
                if (option.InteractionObject != itemInteractable.gameObject) continue;

                bool selected = target.Select(option.Id);
                bool succeeded = selected && target.SubmitSelected();
                Debug.Log($"[InteractionTest] Item selected={selected}, succeeded={succeeded}, active={itemInteractable.gameObject.activeSelf}", this);
                return;
            }

            Debug.LogWarning("[InteractionTest] 当前列表中没有可执行的物品 Option。", this);
        }

        #endregion

        #region 状态验收

        /// <summary>检查选项 ID 唯一、选中项属于列表且列表为空时没有残留选择。</summary>
        [Button("校验列表与选择")]
        public void ValidateListAndSelection()
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;

            bool unique = true;
            bool stableOrder = true;
            for (int left = 0; left < target.Options.Count; left++)
            {
                for (int right = left + 1; right < target.Options.Count; right++)
                {
                    if (target.Options[left].Id != target.Options[right].Id) continue;
                    unique = false;
                }

                if (left + 1 >= target.Options.Count) continue;
                InteractionOption current = target.Options[left];
                InteractionOption next = target.Options[left + 1];
                if (current.Priority < next.Priority ||
                    (current.Priority == next.Priority && current.Id.CompareTo(next.Id) > 0))
                    stableOrder = false;
            }

            bool selectionInList = target.SelectedOption == null;
            for (int index = 0; index < target.Options.Count; index++)
            {
                if (target.SelectedOption == null || target.Options[index].Id != target.SelectedOption.Id) continue;
                selectionInList = true;
                break;
            }

            bool valid = unique && stableOrder && selectionInList &&
                (target.Options.Count > 0 || target.SelectedOption == null);
            Debug.Log($"[InteractionTest] Validate valid={valid}, unique={unique}, stableOrder={stableOrder}, " +
                $"selectionInList={selectionInList}, optionCount={target.Options.Count}", this);
        }

        #endregion

        #region 内部辅助

        /// <summary>解析 Inspector 配置的 PlayerInteractor 并在缺失时输出可定位错误。</summary>
        /// <param name="target">解析出的交互编排组件。</param>
        /// <returns>引用有效时返回 true。</returns>
        private bool TryGetInteractor(out PlayerInteractor target)
        {
            target = interactor;
            if (target != null) return true;

            Debug.LogError("[InteractionTest] 请在 Inspector 配置 PlayerInteractor。", this);
            return false;
        }

        /// <summary>输出当前 Provider、Option 和选择状态，便于人工核对刷新结果。</summary>
        /// <param name="operation">触发日志的测试操作名称。</param>
        private void LogState(string operation)
        {
            if (!TryGetInteractor(out PlayerInteractor target)) return;

            string selectedId = target.SelectedOption == null ? "<none>" : target.SelectedOption.Id.ToString();
            string shapeType = target.Detector.DetectionShape == null
                ? "<none>"
                : target.Detector.DetectionShape.Type.ToString();
            bool canDrawGizmos = target.Detector.DetectionShape != null &&
                target.Detector.DetectionShape.CanDrawGizmos;
            Debug.Log($"[InteractionTest] {operation}: detecting={target.Detector.IsDetecting}, shape={shapeType}, " +
                $"gizmos={canDrawGizmos}, providers={target.Detector.Providers.Count}, " +
                $"options={target.Options.Count}, selected={selectedId}", this);
            for (int index = 0; index < target.Options.Count; index++)
            {
                InteractionOption option = target.Options[index];
                Debug.Log($"[InteractionTest]   [{index}] {option.Id} {option.DisplayName} priority={option.Priority}", this);
            }
        }

        #endregion
    }
}
#endif
