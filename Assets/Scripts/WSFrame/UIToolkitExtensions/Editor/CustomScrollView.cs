using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor
{
    public class CustomScrollView : ScrollView
    {
        public new class UxmlFactory : UxmlFactory<CustomScrollView, UxmlTraits> { }

        public new class UxmlTraits : ScrollView.UxmlTraits { }

        public CustomScrollView()
        {
        }

    }
}