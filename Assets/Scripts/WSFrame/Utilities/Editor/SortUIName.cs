using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    public static class SortUIName
    {
        [MenuItem("GameObject/UI自动绑定工具/整理 UI 名称为 []", false, 0)]
        static void SortUINameByComponent()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("请先选择一个 GameObject");
                return;
            }

            var uiContent = Selection.activeGameObject.transform.Find("UIContent") ?? Selection.activeGameObject.transform;
            if (uiContent == null)
                Debug.LogWarning("选择的对象没有 Transform 组件");
            SortChildrenName(uiContent);
        }

        private static void SortChildrenName(Transform uiContent)
        {
            if (uiContent == null) return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("使用 [] 重命名UI");

            SortChildrenNameInternal(uiContent);

            Undo.CollapseUndoOperations(group);
        }

        private static void SortChildrenNameInternal(Transform uiContent)
        {
            foreach (Transform child in uiContent)
            {
                if (child.gameObject.name == "UIContent") continue;

                if (child.name.Contains("[") && child.name.Contains("]"))
                {
                    SortChildrenNameInternal(child);
                    continue;
                }

                string prefix = GetUIPrefix(child);
                if (!string.IsNullOrEmpty(prefix))
                {
                    Undo.RecordObject(child, "使用 [] 重命名UI");
                    child.name = $"{prefix}{child.name}";
                    EditorUtility.SetDirty(child);
                }

                SortChildrenNameInternal(child);
            }
        }

        private static string GetUIPrefix(Transform child)
        {
            if (child.GetComponent<Button>() != null) return "[Button]";
            if (child.GetComponent<InputField>() != null) return "[InputField]";
            if (child.GetComponent<Dropdown>() != null) return "[Dropdown]";
            if (child.GetComponent<Toggle>() != null) return "[Toggle]";
            if (child.GetComponent<Slider>() != null) return "[Slider]";
            if (child.GetComponent<ScrollRect>() != null) return "[ScrollRect]";
            if (child.GetComponent<Image>() != null) return "[Image]";
            if (child.GetComponent<Text>() != null) return "[Text]";
            if (child.GetComponent<TMP_Text>() != null) return "[TMP_Text]";

            return null;
        }
    }
}