using UnityEngine.UIElements;

namespace WS_Modules
{
    internal interface IInspectorDrawer
    {
        void Draw(object target, VisualElement container);
        void DrawProperty(UnityEngine.Object target, string propertyPath, VisualElement container);
        void DrawUnityProperty(UnityEngine.Object target, string propertyPath, VisualElement container);
    }
}

