using UnityEngine.UIElements;

namespace WS_Modules
{
    public partial class FrameSettingWindow
    {
        private ConfigInstallerView configInstallerView;

        private void DrawConfigInstallerSettings(VisualElement container)
        {
            if (!TryGetFrameSetting(container, out var wsFrameRoot))
            {
                return;
            }

            ConfigInstallerViewModel viewModel = new ConfigInstallerViewModel(wsFrameRoot.FrameSetting);
            configInstallerView = new ConfigInstallerView(container, viewModel);
            configInstallerView.Bind();
        }
    }
}
