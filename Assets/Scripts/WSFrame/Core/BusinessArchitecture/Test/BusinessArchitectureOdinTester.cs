#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace WS_Modules.BusinessArchitecture.Tests
{
    /// <summary>
    /// 基于 Odin Inspector 的 BusinessArchitecture 手动测试组件。
    /// 挂到任意场景对象后，可在 Inspector 中点击按钮观察执行流程和结果。
    /// </summary>
    public sealed class BusinessArchitectureOdinTester : MonoBehaviour
    {
        private const string LogPrefix = "[BusinessArchitectureOdinTest] ";

        [Title("测试参数")]
        [SerializeField, LabelText("Command 增加值")]
        private int addValue = 5;

        [SerializeField, LabelText("返回值 Command 输入")]
        private int calculateInput = 6;

        [SerializeField, LabelText("Query 测试值")]
        private int queryValue = 42;

        [SerializeField, LabelText("事件测试值")]
        private int eventValue = 10;

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

        /// <summary>
        /// 运行全部 BusinessArchitecture 手动测试。
        /// </summary>
        [Button("运行全部测试", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.4f)]
        public void RunAllTests()
        {
            ResetResult("运行全部测试");

            RunScenario("初始化顺序", TestInitializeOrder, false);
            RunScenario("初始化后注册模块", TestRegisterAfterInit, false);
            RunScenario("Command 修改状态", TestCommandMutation, false);
            RunScenario("带返回值 Command", TestResultCommand, false);
            RunScenario("Query 读取状态", TestQueryRead, false);
            RunScenario("事件桥接", TestEventBridge, false);
            RunScenario("手动注销事件", TestManualUnregisterEvent, false);
            RunScenario("Deinit 顺序与重建", TestDeinitOrder, false);

            LogStep($"全部测试完成：通过={passedCount}, 失败={failedCount}");
            FlushTraceText();
        }

        /// <summary>
        /// 测试初始化阶段是否先初始化 Manager，再初始化 System。
        /// </summary>
        [Button("测试：初始化顺序")]
        public void RunInitializeOrderTest()
        {
            RunScenario("初始化顺序", TestInitializeOrder, true);
        }

        /// <summary>
        /// 测试架构初始化完成后新注册的 Manager 和 System 是否会立即初始化。
        /// </summary>
        [Button("测试：初始化后注册模块")]
        public void RunRegisterAfterInitTest()
        {
            RunScenario("初始化后注册模块", TestRegisterAfterInit, true);
        }

        /// <summary>
        /// 测试 Command 是否能通过能力接口访问 Manager 并修改业务状态。
        /// </summary>
        [Button("测试：Command 修改状态")]
        public void RunCommandMutationTest()
        {
            RunScenario("Command 修改状态", TestCommandMutation, true);
        }

        /// <summary>
        /// 测试带返回值的 Command 是否能返回预期计算结果。
        /// </summary>
        [Button("测试：带返回值 Command")]
        public void RunResultCommandTest()
        {
            RunScenario("带返回值 Command", TestResultCommand, true);
        }

        /// <summary>
        /// 测试 Query 是否能读取 Manager 状态并返回结果。
        /// </summary>
        [Button("测试：Query 读取状态")]
        public void RunQueryReadTest()
        {
            RunScenario("Query 读取状态", TestQueryRead, true);
        }

        /// <summary>
        /// 测试 BusinessArchitecture 事件是否桥接到 WSFrame Type 事件中心。
        /// </summary>
        [Button("测试：事件桥接")]
        public void RunEventBridgeTest()
        {
            RunScenario("事件桥接", TestEventBridge, true);
        }

        /// <summary>
        /// 测试 UnRegisterEvent 是否能移除指定事件处理函数。
        /// </summary>
        [Button("测试：手动注销事件")]
        public void RunManualUnregisterEventTest()
        {
            RunScenario("手动注销事件", TestManualUnregisterEvent, true);
        }

        /// <summary>
        /// 测试 Deinit 是否先反初始化 System，再反初始化 Manager，并允许重新创建架构。
        /// </summary>
        [Button("测试：Deinit 顺序与重建")]
        public void RunDeinitOrderTest()
        {
            RunScenario("Deinit 顺序与重建", TestDeinitOrder, true);
        }

        /// <summary>
        /// 清理测试产生的架构单例和 WSFrame 全局事件监听。
        /// </summary>
        [Button("清理测试环境"), GUIColor(1.0f, 0.75f, 0.25f)]
        public void ClearTestEnvironment()
        {
            ResetResult("清理测试环境");
            ResetEnvironment();
            LogStep("测试环境已清理");
            FlushTraceText();
        }

        private void RunScenario(string scenarioName, Action testAction, bool resetResult)
        {
            if (resetResult)
            {
                ResetResult(scenarioName);
            }

            lastScenario = scenarioName;
            LogStep($"开始测试：{scenarioName}");
            ResetEnvironment();

            try
            {
                testAction.Invoke();
                LogStep($"结束测试：{scenarioName}");
            }
            catch (Exception exception)
            {
                Fail($"测试抛出异常：{exception.GetType().Name} - {exception.Message}");
                Debug.LogException(exception, this);
            }
            finally
            {
                FlushTraceText();
            }
        }

        private void TestInitializeOrder()
        {
            var architecture = TestArchitecture.Interface;
            var manager = architecture.GetManager<TestManager>();
            var system = architecture.GetSystem<TestSystem>();
            var utility = architecture.GetUtility<TestUtility>();

            Check(ReferenceEquals(manager, TestArchitecture.DefaultManager), "GetManager 取回默认 Manager 实例");
            Check(ReferenceEquals(system, TestArchitecture.DefaultSystem), "GetSystem 取回默认 System 实例");
            Check(ReferenceEquals(utility, TestArchitecture.DefaultUtility), "GetUtility 取回默认 Utility 实例");
            Check(manager.Initialized, "Manager 已初始化");
            Check(system.Initialized, "System 已初始化");
            CheckOrder("Architecture.Init", "Manager.Init: DefaultManager", "Architecture.Init 早于 Manager.Init");
            CheckOrder("Manager.Init: DefaultManager", "System.Init: DefaultSystem", "Manager.Init 早于 System.Init");
        }

        private void TestRegisterAfterInit()
        {
            var architecture = TestArchitecture.Interface;
            var runtimeManager = new TestManager("RuntimeManager");
            var runtimeSystem = new TestSystem("RuntimeSystem");

            LogStep("运行时注册 Manager");
            architecture.RegisterManager(runtimeManager);
            LogStep("运行时注册 System");
            architecture.RegisterSystem(runtimeSystem);

            Check(runtimeManager.Initialized, "运行时注册的 Manager 已立即初始化");
            Check(runtimeSystem.Initialized, "运行时注册的 System 已立即初始化");
            Check(ReferenceEquals(architecture.GetManager<TestManager>(), runtimeManager), "IOC 中的 Manager 已替换为运行时实例");
            Check(ReferenceEquals(architecture.GetSystem<TestSystem>(), runtimeSystem), "IOC 中的 System 已替换为运行时实例");
            CheckOrder("运行时注册 Manager", "Manager.Init: RuntimeManager", "运行时注册 Manager 后立即 Init");
            CheckOrder("运行时注册 System", "System.Init: RuntimeSystem", "运行时注册 System 后立即 Init");
        }

        private void TestCommandMutation()
        {
            var architecture = TestArchitecture.Interface;
            var manager = architecture.GetManager<TestManager>();
            manager.Value = 3;

            LogStep($"执行前 Value={manager.Value}");
            architecture.SendCommand(new AddValueCommand(addValue));
            LogStep($"执行后 Value={manager.Value}");

            Check(manager.Value == 3 + addValue, $"Command 修改状态正确，期望={3 + addValue}，实际={manager.Value}");
        }

        private void TestResultCommand()
        {
            var architecture = TestArchitecture.Interface;
            var manager = architecture.GetManager<TestManager>();
            manager.Value = 7;

            var result = architecture.SendCommand(new CalculateCommand(calculateInput));
            var expected = 7 + calculateInput * TestArchitecture.DefaultUtility.Multiplier;
            LogStep($"返回值 Command 结果={result}, 期望={expected}");

            Check(result == expected, "带返回值 Command 返回预期结果");
        }

        private void TestQueryRead()
        {
            var architecture = TestArchitecture.Interface;
            architecture.GetManager<TestManager>().Value = queryValue;

            var result = architecture.SendQuery(new GetValueQuery());
            LogStep($"Query 读取结果={result}, 期望={queryValue}");

            Check(result == queryValue, "Query 返回 Manager 当前状态");
        }

        private void TestEventBridge()
        {
            var architecture = TestArchitecture.Interface;
            var receivedCount = 0;
            var receivedValue = 0;

            LogStep("注册 TestBusinessEvent 监听");
            var unregister = architecture.RegisterEvent<TestBusinessEvent>(e =>
            {
                receivedCount++;
                receivedValue = e.Value;
                LogStep($"收到事件 Value={e.Value}, Count={receivedCount}");
            });

            LogStep($"发送 TestBusinessEvent Value={eventValue}");
            architecture.SendEvent(new TestBusinessEvent { Value = eventValue });

            Check(receivedCount == 1, "事件监听收到一次事件");
            Check(receivedValue == eventValue, "事件数据值正确");

            LogStep("通过 IUnRegister 注销事件监听");
            unregister.UnRegister();
            LogStep($"注销后再次发送 TestBusinessEvent Value={eventValue + 1}");
            architecture.SendEvent(new TestBusinessEvent { Value = eventValue + 1 });

            Check(receivedCount == 1, "IUnRegister 后不再收到事件");
            Check(receivedValue == eventValue, "IUnRegister 后事件值未被覆盖");
        }

        private void TestManualUnregisterEvent()
        {
            var architecture = TestArchitecture.Interface;
            var receivedCount = 0;
            Action<TestBusinessEvent> handler = e =>
            {
                receivedCount++;
                LogStep($"收到事件 Value={e.Value}, Count={receivedCount}");
            };

            LogStep("注册事件 handler");
            architecture.RegisterEvent(handler);
            LogStep("手动注销事件 handler");
            architecture.UnRegisterEvent(handler);
            LogStep("注销后发送默认 TestBusinessEvent");
            architecture.SendEvent<TestBusinessEvent>();

            Check(receivedCount == 0, "UnRegisterEvent 后 handler 没有被触发");
        }

        private void TestDeinitOrder()
        {
            var architecture = TestArchitecture.Interface;
            var manager = architecture.GetManager<TestManager>();
            var system = architecture.GetSystem<TestSystem>();

            LogStep("调用 Architecture.Deinit");
            architecture.Deinit();

            Check(!system.Initialized, "System 已反初始化");
            Check(!manager.Initialized, "Manager 已反初始化");
            CheckOrder("System.Deinit: DefaultSystem", "Manager.Deinit: DefaultManager", "System.Deinit 早于 Manager.Deinit");

            LogStep("重新访问 TestArchitecture.Interface");
            var nextArchitecture = TestArchitecture.Interface;
            Check(!ReferenceEquals(nextArchitecture, architecture), "Deinit 后重新创建了新的 Architecture 实例");
            Check(nextArchitecture.GetManager<TestManager>().Initialized, "重新创建后的 Manager 已初始化");
        }

        private void ResetResult(string scenarioName)
        {
            lastScenario = scenarioName;
            passedCount = 0;
            failedCount = 0;
            trace.Clear();
            traceText = string.Empty;
        }

        private void ResetEnvironment()
        {
            TestArchitecture.ResetForManualTest();
            WSEventSystem.Clear();
        }

        private void Check(bool condition, string message)
        {
            if (condition)
            {
                passedCount++;
                LogStep("通过：" + message);
                return;
            }

            Fail("失败：" + message);
        }

        private void CheckOrder(string before, string after, string message)
        {
            var beforeIndex = trace.IndexOf(before);
            var afterIndex = trace.IndexOf(after);
            Check(beforeIndex >= 0, "存在步骤：" + before);
            Check(afterIndex >= 0, "存在步骤：" + after);
            Check(beforeIndex >= 0 && afterIndex >= 0 && beforeIndex < afterIndex, message);
        }

        private void Fail(string message)
        {
            failedCount++;
            LogStep(message);
            Debug.LogError(LogPrefix + message + "\nTrace: " + string.Join(" -> ", trace), this);
        }

        private void LogStep(string message)
        {
            trace.Add(message);
            Debug.Log(LogPrefix + message, this);
        }

        private void FlushTraceText()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < trace.Count; i++)
            {
                builder.Append(i + 1).Append(". ").AppendLine(trace[i]);
            }

            traceText = builder.ToString();
        }

        private sealed class TestArchitecture : Architecture<TestArchitecture>
        {
            public static TestManager DefaultManager { get; private set; }
            public static TestSystem DefaultSystem { get; private set; }
            public static TestUtility DefaultUtility { get; private set; }

            public static void ResetForManualTest()
            {
                if (architecture != null)
                {
                    architecture.Deinit();
                }

                OnRegisterPatch = _ => { };
                DefaultManager = null;
                DefaultSystem = null;
                DefaultUtility = null;
            }

            protected override void Init()
            {
                var tester = FindObjectOfType<BusinessArchitectureOdinTester>();
                tester?.LogStep("Architecture.Init");

                DefaultManager = new TestManager("DefaultManager");
                DefaultSystem = new TestSystem("DefaultSystem");
                DefaultUtility = new TestUtility(2);

                RegisterManager(DefaultManager);
                RegisterSystem(DefaultSystem);
                RegisterUtility(DefaultUtility);
            }

            protected override void OnDeinit()
            {
                var tester = FindObjectOfType<BusinessArchitectureOdinTester>();
                tester?.LogStep("Architecture.Deinit");
            }
        }

        private sealed class TestManager : AbstractManager
        {
            private readonly string name;

            public TestManager(string name)
            {
                this.name = name;
            }

            public int Value { get; set; }

            protected override void OnInit()
            {
                FindObjectOfType<BusinessArchitectureOdinTester>()?.LogStep("Manager.Init: " + name);
            }

            protected override void OnDeinit()
            {
                FindObjectOfType<BusinessArchitectureOdinTester>()?.LogStep("Manager.Deinit: " + name);
            }
        }

        private sealed class TestSystem : AbstractSystem
        {
            private readonly string name;

            public TestSystem(string name)
            {
                this.name = name;
            }

            protected override void OnInit()
            {
                var manager = this.GetManager<TestManager>();
                var tester = FindObjectOfType<BusinessArchitectureOdinTester>();
                tester?.LogStep($"System.Init: {name}");
                tester?.LogStep($"System.Init 读取 Manager.Value={manager.Value}");
            }

            protected override void OnDeinit()
            {
                FindObjectOfType<BusinessArchitectureOdinTester>()?.LogStep("System.Deinit: " + name);
            }
        }

        private sealed class TestUtility : IUtility
        {
            public TestUtility(int multiplier)
            {
                Multiplier = multiplier;
            }

            public int Multiplier { get; }

            public int Multiply(int value)
            {
                return value * Multiplier;
            }
        }

        private sealed class AddValueCommand : AbstractCommand
        {
            private readonly int addValue;

            public AddValueCommand(int addValue)
            {
                this.addValue = addValue;
            }

            protected override void OnExecute()
            {
                var manager = this.GetManager<TestManager>();
                var tester = FindObjectOfType<BusinessArchitectureOdinTester>();
                tester?.LogStep($"Command.Execute: before={manager.Value}, add={addValue}");
                manager.Value += addValue;
                tester?.LogStep($"Command.Execute: after={manager.Value}");
            }
        }

        private sealed class CalculateCommand : AbstractCommand<int>
        {
            private readonly int input;

            public CalculateCommand(int input)
            {
                this.input = input;
            }

            protected override int OnExecute()
            {
                var manager = this.GetManager<TestManager>();
                var utility = this.GetUtility<TestUtility>();
                var result = manager.Value + utility.Multiply(input);
                FindObjectOfType<BusinessArchitectureOdinTester>()?.LogStep(
                    $"Command<int>.Execute: manager={manager.Value}, input={input}, result={result}");
                return result;
            }
        }

        private sealed class GetValueQuery : AbstractQuery<int>
        {
            protected override int OnDo()
            {
                var value = this.GetManager<TestManager>().Value;
                FindObjectOfType<BusinessArchitectureOdinTester>()?.LogStep($"Query.Do: value={value}");
                return value;
            }
        }

        private sealed class TestBusinessEvent
        {
            public int Value;
        }
    }
}
#endif
