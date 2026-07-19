using System;
using UnityEngine.UIElements;

namespace WS_Modules
{
    internal sealed class FrameModuleDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Order { get; }
        public bool Enabled { get; }
        public Action<VisualElement> DrawAction { get; }

        public FrameModuleDescriptor(string id, string displayName, int order, Action<VisualElement> drawAction,
            bool enabled = true)
        {
            Id = id;
            DisplayName = displayName;
            Order = order;
            DrawAction = drawAction;
            Enabled = enabled;
        }
    }
}

