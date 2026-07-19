using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor
{
    public class CustomTwoPanelSplitView : TwoPaneSplitView
    {
        public new class UxmlFactory : UxmlFactory<CustomTwoPanelSplitView, UxmlTraits> { }

        public new class UxmlTraits : TwoPaneSplitView.UxmlTraits { }

        public CustomTwoPanelSplitView()
        {
        }

        public CustomTwoPanelSplitView(int fixedPaneIndex, int fixedPaneInitialDimension, TwoPaneSplitViewOrientation orientation) 
            : base(fixedPaneIndex, fixedPaneInitialDimension, orientation)
        {
        }
    }
}