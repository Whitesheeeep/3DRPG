#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.FSM.Tests
{
    /// <summary>
    /// 基于 Odin Inspector 手动验证 UnifiedFSM 同级、向上请求和路径切换行为。
    /// </summary>
    public sealed class UnifiedFSMOdinTester : MonoBehaviour
    {
        #region 测试结果

        [Title("测试结果")]
        [ShowInInspector, ReadOnly, LabelText("最后运行项目")]
        private string lastScenario = "未运行";

        [ShowInInspector, ReadOnly, LabelText("通过数量")]
        private int passedCount;

        [ShowInInspector, ReadOnly, LabelText("失败数量")]
        private int failedCount;

        [ShowInInspector, ReadOnly, MultiLineProperty(12), LabelText("执行轨迹")]
        private string traceText = string.Empty;

        private readonly List<string> trace = new List<string>();

        #endregion

        #region Odin 操作

        /// <summary>
        /// 运行 UnifiedFSM 的全部层级路由手动测试。
        /// </summary>
        [Button("运行全部测试", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.4f)]
        public void RunAllTests()
        {
            ResetResults("运行全部测试");
            RunScenario("同级切换", TestSameLevelChange);
            RunScenario("子状态向父级请求", TestRequestStateChange);
            RunScenario("按路径进入孙状态", TestStatePathChange);
            RunScenario("无效路径保持现状", TestInvalidStatePath);
            FlushTraceText();
        }

        /// <summary>
        /// 验证当前状态机可以在直接子状态之间切换。
        /// </summary>
        [Button("测试：同级切换")]
        public void RunSameLevelTest()
        {
            RunSingleScenario("同级切换", TestSameLevelChange);
        }

        /// <summary>
        /// 验证子状态机找不到目标时，会将请求交给父状态机处理。
        /// </summary>
        [Button("测试：子状态向父级请求")]
        public void RunRequestStateChangeTest()
        {
            RunSingleScenario("子状态向父级请求", TestRequestStateChange);
        }

        /// <summary>
        /// 验证根状态机可以沿层级路径进入嵌套状态。
        /// </summary>
        [Button("测试：按路径进入孙状态")]
        public void RunStatePathTest()
        {
            RunSingleScenario("按路径进入孙状态", TestStatePathChange);
        }

        /// <summary>
        /// 验证路径中存在非子状态时不会部分改变状态机。
        /// </summary>
        [Button("测试：无效路径保持现状")]
        public void RunInvalidStatePathTest()
        {
            RunSingleScenario("无效路径保持现状", TestInvalidStatePath);
        }

        /// <summary>
        /// 清空 Inspector 中显示的测试结果和执行轨迹。
        /// </summary>
        [Button("清理测试结果"), GUIColor(1.0f, 0.75f, 0.25f)]
        public void ClearResults()
        {
            ResetResults("清理测试结果");
            FlushTraceText();
        }

        #endregion

        #region 测试场景

        // 执行单个场景并统一捕获异常，保证 Inspector 按钮能显示完整结果。
        private void RunSingleScenario(string scenarioName, Func<bool> testAction)
        {
            ResetResults(scenarioName);
            RunScenario(scenarioName, testAction);
            FlushTraceText();
        }

        // 调用真实 UnifiedFSM API，并将每个场景的结果写入 Inspector 和 Console。
        private void RunScenario(string scenarioName, Func<bool> testAction)
        {
            lastScenario = scenarioName;
            LogStep($"开始测试：{scenarioName}");

            try
            {
                bool passed = testAction.Invoke();
                if (passed)
                {
                    passedCount++;
                    LogStep($"通过：{scenarioName}");
                }
                else
                {
                    failedCount++;
                    LogFailure($"失败：{scenarioName}");
                }
            }
            catch (Exception exception)
            {
                failedCount++;
                LogFailure($"异常：{exception.GetType().Name} - {exception.Message}");
                Debug.LogException(exception, this);
            }
        }

        // 验证根状态机的直接子状态切换。
        private bool TestSameLevelChange()
        {
            CreateStateMachines(out var root, out _);
            bool changed = root.ChangeState(TestStateId.Attack);
            bool correctState = IsState(root.CurrentState, TestStateId.Attack);
            LogStep($"ChangeState(Attack)：changed={changed}, current={Describe(root.CurrentState)}");
            return changed && correctState;
        }

        // 验证嵌套状态机向父级请求兄弟状态，并递归退出原子状态机。
        private bool TestRequestStateChange()
        {
            CreateStateMachines(out var root, out var grounded);
            root.ChangeState(TestStateId.Grounded);
            bool requested = grounded.RequestStateChange(TestStateId.Attack);
            bool correctRoot = IsState(root.CurrentState, TestStateId.Attack);
            bool exitedChild = grounded.CurrentState == null;
            LogStep($"RequestStateChange(Attack)：requested={requested}, root={Describe(root.CurrentState)}, grounded={Describe(grounded.CurrentState)}");
            return requested && correctRoot && exitedChild;
        }

        // 验证路径 API 会先进入父状态，再进入指定的孙状态。
        private bool TestStatePathChange()
        {
            CreateStateMachines(out var root, out var grounded);
            bool changed = root.ChangeStatePath(TestStateId.Grounded, TestStateId.Run);
            bool correctRoot = IsState(root.CurrentState, TestStateId.Grounded);
            bool correctChild = IsState(grounded.CurrentState, TestStateId.Run);
            LogStep($"ChangeStatePath(Grounded, Run)：changed={changed}, root={Describe(root.CurrentState)}, grounded={Describe(grounded.CurrentState)}");
            return changed && correctRoot && correctChild;
        }

        // 验证路径预检查失败时，根状态机仍保持默认 Idle。
        private bool TestInvalidStatePath()
        {
            CreateStateMachines(out var root, out _);
            bool changed = root.ChangeStatePath(TestStateId.Grounded, TestStateId.Attack);
            bool unchanged = IsState(root.CurrentState, TestStateId.Idle);
            LogStep($"ChangeStatePath(Grounded, Attack)：changed={changed}, current={Describe(root.CurrentState)}");
            return !changed && unchanged;
        }

        // 创建与业务无关的纯内存 HFSM，按钮测试结束后不会在场景中留下运行时对象。
        private static void CreateStateMachines(
            out StateMachine<TestStateId, TestOwner> root,
            out StateMachine<TestStateId, TestOwner> grounded)
        {
            var owner = new TestOwner();
            root = new StateMachine<TestStateId, TestOwner>(TestStateId.Root, owner);
            grounded = new StateMachine<TestStateId, TestOwner>(TestStateId.Grounded);

            root.AddState(new TestState(TestStateId.Idle));
            root.AddState(grounded);
            root.AddState(new TestState(TestStateId.Attack));

            grounded.AddState(new TestState(TestStateId.Walk));
            grounded.AddState(new TestState(TestStateId.Run));

            grounded.SetDefaultState(TestStateId.Walk);
            root.SetDefaultState(TestStateId.Idle);
            root.OnEnter();
        }

        #endregion

        #region 测试辅助

        // 重置本次 Inspector 手动测试的累计结果。
        private void ResetResults(string scenarioName)
        {
            lastScenario = scenarioName;
            passedCount = 0;
            failedCount = 0;
            trace.Clear();
            traceText = string.Empty;
        }

        // 记录普通测试步骤，同时输出到 Unity Console。
        private void LogStep(string message)
        {
            trace.Add(message);
            Debug.Log("[UnifiedFSMTest] " + message, this);
        }

        // 记录失败步骤并输出带组件上下文的错误日志。
        private void LogFailure(string message)
        {
            trace.Add(message);
            Debug.LogError("[UnifiedFSMTest] " + message, this);
        }

        // 将步骤列表同步到 Inspector 多行文本字段。
        private void FlushTraceText()
        {
            traceText = string.Join("\n", trace);
        }

        // 比较当前状态 ID，统一处理尚未进入状态的 null 情况。
        private static bool IsState(
            IState<TestStateId, TestOwner> state,
            TestStateId expected)
        {
            return state != null && state.StateId == expected;
        }

        // 生成便于人工核对层级的当前状态文本。
        private static string Describe(IState<TestStateId, TestOwner> state)
        {
            return state == null ? "<none>" : state.StateId.ToString();
        }

        #endregion

        #region 测试模型

        /// <summary>
        /// 手动测试使用的状态 ID，模拟根状态、复合状态和叶子状态。
        /// </summary>
        private enum TestStateId
        {
            Root,
            Idle,
            Grounded,
            Walk,
            Run,
            Attack
        }

        /// <summary>
        /// 手动测试使用的状态机 Owner 类型。
        /// </summary>
        private sealed class TestOwner
        {
        }

        /// <summary>
        /// 手动测试使用的最小叶子状态实现。
        /// </summary>
        private sealed class TestState : StateBase<TestStateId, TestOwner>
        {
            /// <summary>
            /// 创建指定 ID 的测试叶子状态。
            /// </summary>
            public TestState(TestStateId stateId) : base(stateId)
            {
            }
        }

        #endregion
    }
}
#endif