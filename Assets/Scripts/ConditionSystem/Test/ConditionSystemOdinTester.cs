#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace RPG.ConditionSystemNS.Tests
{
    /// <summary>通过 Odin 按钮或菜单验证真实条件 API；全部临时对象在 finally 中释放。</summary>
    public sealed class ConditionSystemOdinTester : MonoBehaviour
    {
        #region 测试状态
        [ShowInInspector, ReadOnly] private string lastResult = "尚未验证";
        #endregion

        #region 手动入口
        /// <summary>在 Edit Mode 或 Play Mode 验证运行时逻辑，不依赖场景组件。</summary>
        [Button("验证条件组合、扩展与非法配置", ButtonSizes.Large)]
        public void RunTests()
        {
            RunRuntimeVerification();
            lastResult = "全部运行时断言通过";
        }

        /// <summary>提供不创建场景测试组件的同等入口，便于编辑器交互验收。</summary>
        [MenuItem("RPG/Condition/Run Runtime Verification", priority = 120)]
        public static void RunRuntimeVerification()
        {
            var objects = new List<UnityEngine.Object>();
            Debug.Log("[ConditionSystemTest] 开始运行时验证；最后一项预期产生一条配置错误日志。");
            try
            {
                VerifyCombinations(objects);
                VerifyContextAndReasons(objects);
                VerifyInvalidGraphs(objects);
                Debug.Log("[ConditionSystemTest] PASS：组合、短路、顺序、派生上下文/原因、重复求值与非法配置全部通过。");
            }
            finally
            {
                for (int index = objects.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(objects[index]);
                Debug.Log($"[ConditionSystemTest] 释放临时对象完成，count={objects.Count}");
            }
        }

        /// <summary>保存、重导入并重新载入独立临时资产，验证子资产、顺序和布局；完成后删除测试资源。</summary>
        [Button("验证资产保存与重载", ButtonSizes.Large)]
        [MenuItem("RPG/Condition/Run Asset Round Trip Verification", priority = 121)]
        public static void RunAssetRoundTripVerification()
        {
            const string folder = "Assets/Scripts/ConditionSystem/Test";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/ConditionRoundTrip.asset");
            var graph = ScriptableObject.CreateInstance<ConditionGraph_SO>();
            var root = ScriptableObject.CreateInstance<AndConditionNode>();
            var first = ScriptableObject.CreateInstance<ConstantConditionNode>();
            var second = ScriptableObject.CreateInstance<ConstantConditionNode>();
            bool persisted = false;
            try
            {
                graph.name = "ConditionRoundTrip";
                AssetDatabase.CreateAsset(graph, path);
                persisted = true;
                foreach (ConditionNode node in new ConditionNode[] { root, first, second })
                {
                    node.name = node.GetType().Name;
                    AssetDatabase.AddObjectToAsset(node, graph);
                    graph.AddNode(node);
                }
                graph.SetRoot(root);
                graph.Connect(root, first);
                graph.Connect(root, second);
                root.MoveChild(0, 1);
                first.Value = false;
                root.EditorPosition = new Vector2(123, 456);
                string firstId = first.NodeId;
                string secondId = second.NodeId;
                EditorUtility.SetDirty(graph);
                foreach (ConditionNode node in graph.Nodes) EditorUtility.SetDirty(node);
                AssetDatabase.SaveAssets();
                // 卸载旧对象后从磁盘还原，避免只检查内存中同一实例。
                Resources.UnloadAsset(graph);
                Resources.UnloadAsset(root);
                Resources.UnloadAsset(first);
                Resources.UnloadAsset(second);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                graph = AssetDatabase.LoadAssetAtPath<ConditionGraph_SO>(path);
                Require(graph.Root is AndConditionNode && graph.Nodes.Count == 3, "主资产与子资产还原失败");
                Require(graph.Root.Children[0].NodeId == secondId && graph.Root.Children[1].NodeId == firstId, "子条件顺序未保存");
                Require(graph.Root.EditorPosition == new Vector2(123, 456), "内容坐标未保存");
                Require(!graph.Evaluate(new TestContext(0)).IsMet, "节点参数或所有权引用未还原");
                Require(AssetDatabase.LoadAllAssetsAtPath(path).OfType<ConditionNode>().Count() == 3, "节点子资产数量错误");
                Debug.Log("[ConditionSystemTest] PASS：保存重载、节点 ID、参数、顺序、位置和子资产引用全部通过。");
            }
            finally
            {
                // 只删除本次生成的精确测试资产，不递归删除目录或触碰用户配置。
                if (persisted)
                {
                    if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException("[ConditionSystemTest] 临时测试资产删除失败。");
                }
                else
                {
                    foreach (UnityEngine.Object value in new UnityEngine.Object[] { second, first, root, graph })
                        if (value != null) UnityEngine.Object.DestroyImmediate(value);
                }
                Debug.Log("[ConditionSystemTest] 临时保存重载资产已清理。");
            }
        }
        #endregion

        #region 组合与扩展验证
        // 组合语义：顺序、短路、嵌套和否定原因。
        /// <summary>验证实际组合、短路顺序和独立 Not 原因。</summary>
        /// <param name="objects">需要释放的临时对象。</param>
        private static void VerifyCombinations(List<UnityEngine.Object> objects)
        {
            ConditionGraph_SO graph = CreateGraph(objects);
            AndConditionNode all = Add<AndConditionNode>(graph, objects);
            TestValueNode first = Add<TestValueNode>(graph, objects);
            TestValueNode second = Add<TestValueNode>(graph, objects);
            first.Required = 10;
            second.Required = 20;
            graph.SetRoot(all);
            graph.Connect(all, first);
            graph.Connect(all, second);
            var context = new TestContext(5);
            var reason = graph.Evaluate(context).Reason as CompositeConditionReason;
            Require(reason != null && reason.Operator == E_ConditionReasonOperator.And && reason.Children.Count == 2,
                "And 未收集全部原因");
            Require(reason.Children[0] is TestReason && reason.Children[1] is TestReason, "组合过程丢失了原因派生类型");
            Require(context.Visits.SequenceEqual(new[] { first.NodeId, second.NodeId }), "And 求值顺序错误");
            all.MoveChild(0, 1);
            context.Visits.Clear();
            graph.Evaluate(context);
            Require(context.Visits.SequenceEqual(new[] { second.NodeId, first.NodeId }), "顺序调整没有作用");
            Require(graph.Evaluate(new TestContext(30)).IsMet, "And 全满足判断错误");

            ConditionGraph_SO orGraph = CreateGraph(objects);
            OrConditionNode any = Add<OrConditionNode>(orGraph, objects);
            TestValueNode pass = Add<TestValueNode>(orGraph, objects);
            TestValueNode skipped = Add<TestValueNode>(orGraph, objects);
            pass.Required = 1;
            skipped.Required = 10;
            orGraph.SetRoot(any);
            orGraph.Connect(any, pass);
            orGraph.Connect(any, skipped);
            context = new TestContext(5);
            Require(orGraph.Evaluate(context).IsMet && context.Visits.Count == 1, "Or 没有按顺序短路");
            reason = (CompositeConditionReason)orGraph.Evaluate(new TestContext(0)).Reason;
            Require(reason.Operator == E_ConditionReasonOperator.Or && reason.Children.Count == 2, "Or 未保留分支原因");

            ConditionGraph_SO notGraph = CreateGraph(objects);
            NotConditionNode not = Add<NotConditionNode>(notGraph, objects);
            ConstantConditionNode constant = Add<ConstantConditionNode>(notGraph, objects);
            notGraph.SetRoot(not);
            notGraph.Connect(not, constant);
            reason = (CompositeConditionReason)notGraph.Evaluate(new TestContext(0)).Reason;
            Require(reason.Operator == E_ConditionReasonOperator.Not && reason.Children.Count == 0, "Not 原因语义错误");
            constant.Value = false;
            Require(notGraph.Evaluate(new TestContext(0)).IsMet, "Not 没有反转失败状态");

            // 将已有独立逻辑树组合为 And -> Or -> Not，验证递归不会重复整图校验或修改配置。
            ConditionGraph_SO nested = CreateGraph(objects);
            AndConditionNode nestedAll = Add<AndConditionNode>(nested, objects);
            OrConditionNode nestedAny = Add<OrConditionNode>(nested, objects);
            NotConditionNode nestedNot = Add<NotConditionNode>(nested, objects);
            ConstantConditionNode nestedTrue = Add<ConstantConditionNode>(nested, objects);
            ConstantConditionNode nestedFalse = Add<ConstantConditionNode>(nested, objects);
            nestedFalse.Value = false;
            nested.SetRoot(nestedAll);
            nested.Connect(nestedAll, nestedAny);
            nested.Connect(nestedAny, nestedNot);
            nested.Connect(nestedAny, nestedTrue);
            nested.Connect(nestedNot, nestedFalse);
            Require(nested.Evaluate(new TestContext(0)).IsMet, "嵌套 And/Or/Not 求值错误");
        }

        // 上下文与原因扩展：共享配置不保存本次输入或上次结果。
        /// <summary>验证不同业务上下文共享配置，且派生原因不被组合过程丢失。</summary>
        /// <param name="objects">需要释放的临时对象。</param>
        private static void VerifyContextAndReasons(List<UnityEngine.Object> objects)
        {
            ConditionGraph_SO graph = CreateGraph(objects);
            TestValueNode node = Add<TestValueNode>(graph, objects);
            node.Required = 100;
            graph.SetRoot(node);
            string before = EditorJsonUtility.ToJson(node);
            var reason = graph.Evaluate(new TestContext(20)).Reason as TestReason;
            Require(reason != null && reason.Required == 100 && reason.Current == 20, "派生原因数据丢失");
            Require(graph.Evaluate(new TestContext(120)).IsMet && !graph.Evaluate(new TestContext(0)).IsMet,
                "不同上下文互相污染");
            Require(before == EditorJsonUtility.ToJson(node), "求值修改了 SO 配置");
        }
        #endregion

        #region 配置边界验证
        /// <summary>通过序列化边界注入损坏数据，验证真实校验能阻止无效图进入求值。</summary>
        /// <param name="objects">需要释放的临时对象。</param>
        private static void VerifyInvalidGraphs(List<UnityEngine.Object> objects)
        {
            ConditionGraph_SO missing = CreateGraph(objects);
            RequireInvalid(missing, "缺根");
            ConditionGraph_SO emptyOrGraph = CreateGraph(objects);
            emptyOrGraph.SetRoot(Add<OrConditionNode>(emptyOrGraph, objects));
            RequireInvalid(emptyOrGraph, "空 Or");
            ConditionGraph_SO graph = CreateGraph(objects);
            NotConditionNode root = Add<NotConditionNode>(graph, objects);
            AndConditionNode empty = Add<AndConditionNode>(graph, objects);
            ConstantConditionNode leaf = Add<ConstantConditionNode>(graph, objects);
            graph.SetRoot(root);
            RequireInvalid(graph, "空 Not/And");
            graph.Connect(root, empty);
            graph.Connect(empty, leaf);
            Require(graph.GetConnectionIssue(empty, root) != null, "没有拒绝根入向连接");
            Require(graph.GetConnectionIssue(empty, empty) != null, "没有拒绝自连接");
            Require(graph.GetConnectionIssue(root, leaf) != null, "没有拒绝容量/多父连接");
            SetChildren(empty, leaf, null);
            RequireInvalid(graph, "空子引用");
            SetChildren(empty, leaf, leaf);
            RequireInvalid(graph, "重复引用");
            SetChildren(empty, root);
            RequireInvalid(graph, "循环");
            ConditionGraph_SO foreign = CreateGraph(objects);
            ConstantConditionNode foreignLeaf = Add<ConstantConditionNode>(foreign, objects);
            SetChildren(empty, foreignLeaf);
            RequireInvalid(graph, "跨图引用");
            SetChildren(empty, leaf);
            OrConditionNode secondParent = Add<OrConditionNode>(graph, objects);
            SetChildren(secondParent, leaf);
            RequireInvalid(graph, "多父节点");
            SetChildren(secondParent, leaf);
            SetChildren(empty);
            // 此图在 Not 下放置空 And，必须抛出配置异常，不能当作 false 再取反通过。
            bool rejected = false;
            try { graph.Evaluate(new TestContext(0)); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "配置异常被 Not 当作普通失败处理");
        }

        /// <summary>确认图级校验明确标记错误，不通过控制台日志猜测结论。</summary>
        /// <param name="graph">待校验图。</param>
        /// <param name="scenario">失败场景。</param>
        private static void RequireInvalid(ConditionGraph_SO graph, string scenario) =>
            Require(graph.GetValidationIssues().Any(issue => issue.Severity == E_ConditionIssueSeverity.Error), scenario + "未被拒绝");

        /// <summary>仅测试序列化损坏边界，正常配置必须走图结构 API。</summary>
        /// <param name="node">目标父节点。</param>
        /// <param name="children">注入的引用集合。</param>
        private static void SetChildren(ConditionNode node, params ConditionNode[] children)
        {
            using (var serialized = new SerializedObject(node))
            {
                SerializedProperty list = serialized.FindProperty("children");
                list.arraySize = children.Length;
                for (int index = 0; index < children.Length; index++) list.GetArrayElementAtIndex(index).objectReferenceValue = children[index];
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        #endregion

        #region 测试辅助与嵌套类型
        // 测试辅助：所有临时对象登记在调用者的释放集合，不保存到场景或配置。
        /// <summary>创建需要最终释放的内存图。</summary>
        /// <param name="objects">释放集合。</param>
        /// <returns>新图。</returns>
        private static ConditionGraph_SO CreateGraph(List<UnityEngine.Object> objects)
        {
            var graph = ScriptableObject.CreateInstance<ConditionGraph_SO>();
            graph.name = "ConditionVerification";
            objects.Add(graph);
            return graph;
        }

        /// <summary>创建并登记具体测试节点。</summary>
        /// <typeparam name="T">具体节点类型。</typeparam>
        /// <param name="graph">所属图。</param>
        /// <param name="objects">释放集合。</param>
        /// <returns>新节点。</returns>
        private static T Add<T>(ConditionGraph_SO graph, List<UnityEngine.Object> objects) where T : ConditionNode
        {
            T node = ScriptableObject.CreateInstance<T>();
            node.name = typeof(T).Name;
            objects.Add(node);
            graph.AddNode(node);
            return node;
        }

        /// <summary>让验证失败立即暴露，不吞掉错误或伪造通过结果。</summary>
        /// <param name="condition">预期成立的断言。</param>
        /// <param name="message">失败上下文。</param>
        /// <exception cref="InvalidOperationException">断言不成立时抛出。</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[ConditionSystemTest] " + message);
        }

        // 嵌套类型：模拟业务扩展，不会作为正式节点出现在编辑器创建菜单。
        /// <summary>显式携带业务值和本次访问顺序的上下文。</summary>
        private sealed class TestContext : ConditionContext
        {
            internal int Value { get; }
            internal List<string> Visits { get; } = new List<string>();
            /// <summary>设置本次输入。</summary>
            /// <param name="value">业务数值。</param>
            internal TestContext(int value) { Value = value; }
        }

        /// <summary>携带数值不足详情的派生原因。</summary>
        private sealed class TestReason : ConditionReason
        {
            internal int Required { get; }
            internal int Current { get; }
            /// <summary>记录真实不足项，不包含业务回调。</summary>
            /// <param name="required">需求数量。</param>
            /// <param name="current">当前数量。</param>
            internal TestReason(int required, int current) : base($"需要 {required}，当前 {current}")
            { Required = required; Current = current; }
        }

        /// <summary>通过显式上下文类型实现读取业务数据的测试叶子节点。</summary>
        private sealed class TestValueNode : ConditionNode
        {
            internal int Required { get; set; }
            /// <summary>读取本次业务值并记录顺序，不将状态写回节点。</summary>
            /// <param name="context">必须为 TestContext。</param>
            /// <returns>包含派生原因的比较结果。</returns>
            public override ConditionResult Evaluate(ConditionContext context)
            {
                var input = (TestContext)context;
                input.Visits.Add(NodeId);
                return input.Value >= Required ? ConditionResult.Met() : ConditionResult.NotMet(new TestReason(Required, input.Value));
            }
        }
        #endregion
    }
}
#endif
