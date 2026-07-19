using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WS_Modules.EditorExtensions
{
    internal sealed class TestCenterViewModel
    {
        private const string ScriptSearchRoot = "Assets/Scripts";
        private const string TestObjectName = "Test";

        private readonly List<TesterViewData> allTesters = new();
        private readonly List<TesterViewData> filteredTesters = new();
        private string searchKeyword = string.Empty;

        public event Action TestersChanged;
        public event Action SelectionChanged;

        public IReadOnlyList<TesterViewData> FilteredTesters => filteredTesters;
        public TesterViewData SelectedTester { get; private set; }
        public string SearchKeyword => searchKeyword;

        public void Refresh()
        {
            Type selectedType = SelectedTester?.TesterType;
            allTesters.Clear();

            Dictionary<Type, MonoScript> testerScripts = FindTesterScripts()
                .Select(script => new
                {
                    Script = script,
                    Type = script.GetClass(),
                    Path = AssetDatabase.GetAssetPath(script)
                })
                .Where(data => IsTesterType(data.Type, data.Path))
                .GroupBy(data => data.Type)
                .ToDictionary(group => group.Key, group => group.First().Script);

            Dictionary<Type, MonoBehaviour> sceneInstances = FindSceneTesterInstances(testerScripts.Keys)
                .GroupBy(instance => instance.GetType())
                .ToDictionary(group => group.Key, group => group.First());

            foreach (KeyValuePair<Type, MonoScript> pair in testerScripts)
            {
                Type testerType = pair.Key;
                MonoScript script = pair.Value;
                sceneInstances.TryGetValue(testerType, out MonoBehaviour instance);
                allTesters.Add(new TesterViewData(
                    testerType,
                    script,
                    AssetDatabase.GetAssetPath(script),
                    instance));
            }

            foreach (MonoBehaviour sceneInstance in sceneInstances.Values)
            {
                Type type = sceneInstance.GetType();
                if (allTesters.Any(tester => tester.TesterType == type))
                {
                    continue;
                }

                allTesters.Add(new TesterViewData(type, null, string.Empty, sceneInstance));
            }

            allTesters.Sort(CompareTester);
            RefreshFilter();
            SelectedTester = filteredTesters.FirstOrDefault(tester => tester.TesterType == selectedType) ?? filteredTesters.FirstOrDefault();
            TestersChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void SetSearchKeyword(string keyword)
        {
            searchKeyword = keyword ?? string.Empty;
            RefreshFilter();

            if (SelectedTester != null && !filteredTesters.Contains(SelectedTester))
            {
                SelectedTester = filteredTesters.FirstOrDefault();
                SelectionChanged?.Invoke();
            }

            TestersChanged?.Invoke();
        }

        public void Select(TesterViewData tester)
        {
            if (SelectedTester == tester)
            {
                return;
            }

            SelectedTester = tester;
            SelectionChanged?.Invoke();
        }

        public GameObject CreateOrFindTestObject()
        {
            GameObject testObject = FindTestObject();
            if (testObject != null)
            {
                Selection.activeGameObject = testObject;
                EditorGUIUtility.PingObject(testObject);
                return testObject;
            }

            testObject = new GameObject(TestObjectName);
            Undo.RegisterCreatedObjectUndo(testObject, "创建 Test 物体");
            EditorSceneManager.MarkSceneDirty(testObject.scene);
            Selection.activeGameObject = testObject;
            EditorGUIUtility.PingObject(testObject);
            return testObject;
        }

        public void LoadSelectedTester()
        {
            if (SelectedTester?.TesterType == null || SelectedTester.IsLoaded)
            {
                return;
            }

            GameObject testObject = CreateOrFindTestObject();
            Component component = Undo.AddComponent(testObject, SelectedTester.TesterType);
            SelectedTester.SetInstance(component as MonoBehaviour);
            EditorSceneManager.MarkSceneDirty(testObject.scene);
            Selection.activeObject = component;
            Refresh();
        }

        public void RemoveSelectedTester()
        {
            MonoBehaviour instance = SelectedTester?.Instance;
            if (instance == null)
            {
                return;
            }

            GameObject owner = instance.gameObject;
            Undo.DestroyObjectImmediate(instance);
            if (owner != null && owner.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(owner.scene);
            }

            Refresh();
        }

        public void PingSelectedScript()
        {
            if (SelectedTester?.Script == null)
            {
                return;
            }

            Selection.activeObject = SelectedTester.Script;
            EditorGUIUtility.PingObject(SelectedTester.Script);
        }

        public void PingSelectedInstance()
        {
            if (SelectedTester?.Instance == null)
            {
                return;
            }

            Selection.activeObject = SelectedTester.Instance;
            EditorGUIUtility.PingObject(SelectedTester.Instance.gameObject);
        }

        private static IEnumerable<MonoScript> FindTesterScripts()
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { ScriptSearchRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && IsTesterPath(path))
                {
                    yield return script;
                }
            }
        }

        private static IEnumerable<MonoBehaviour> FindSceneTesterInstances(IEnumerable<Type> scriptTesterTypes)
        {
            HashSet<Type> scriptTypeSet = new HashSet<Type>(scriptTesterTypes);
            foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null ||
                    EditorUtility.IsPersistent(behaviour) ||
                    behaviour.gameObject == null ||
                    !behaviour.gameObject.scene.IsValid() ||
                    !IsTesterType(behaviour.GetType(), string.Empty, scriptTypeSet))
                {
                    continue;
                }

                yield return behaviour;
            }
        }

        private static bool IsTesterType(Type type, string scriptPath, HashSet<Type> knownTesterTypes = null)
        {
            return type != null &&
                   !type.IsAbstract &&
                   !type.IsGenericType &&
                   typeof(MonoBehaviour).IsAssignableFrom(type) &&
                   (knownTesterTypes?.Contains(type) == true ||
                    type.Name.EndsWith("Tester", StringComparison.Ordinal) ||
                    type.Name.EndsWith("OdinTester", StringComparison.Ordinal) ||
                    IsTesterPath(scriptPath));
        }

        private static bool IsTesterPath(string path)
        {
            return path.IndexOf("/Test/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("\\Test\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.EndsWith("Tester.cs", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("OdinTester.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject FindTestObject()
        {
            foreach (GameObject gameObject in Object.FindObjectsOfType<GameObject>(true))
            {
                if (gameObject.scene.IsValid() && gameObject.name == TestObjectName)
                {
                    return gameObject;
                }
            }

            return null;
        }

        private void RefreshFilter()
        {
            filteredTesters.Clear();
            string keyword = searchKeyword.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                filteredTesters.AddRange(allTesters);
                return;
            }

            string lowerKeyword = keyword.ToLowerInvariant();
            foreach (TesterViewData tester in allTesters)
            {
                if (tester.TypeName.ToLowerInvariant().Contains(lowerKeyword) ||
                    tester.NamespaceName.ToLowerInvariant().Contains(lowerKeyword) ||
                    tester.ScriptPath.ToLowerInvariant().Contains(lowerKeyword))
                {
                    filteredTesters.Add(tester);
                }
            }
        }

        private static int CompareTester(TesterViewData left, TesterViewData right)
        {
            int loadedCompare = right.IsLoaded.CompareTo(left.IsLoaded);
            if (loadedCompare != 0)
            {
                return loadedCompare;
            }

            return string.Compare(left.TypeName, right.TypeName, StringComparison.Ordinal);
        }
    }
}
