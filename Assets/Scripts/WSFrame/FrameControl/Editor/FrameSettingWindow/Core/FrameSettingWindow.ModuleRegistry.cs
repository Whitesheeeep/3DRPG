namespace WS_Modules
{
    public partial class FrameSettingWindow
    {
        private FrameModuleRegistry BuildDefaultModuleRegistry()
        {
            var registry = new FrameModuleRegistry();

            registry.Register(new FrameModuleDescriptor("FrameRoot", "FrameRoot", 0, DrawFrameRootSettings));
            registry.Register(new FrameModuleDescriptor("LogModule", "LogModule", 1, DrawLogSettings));
            registry.Register(new FrameModuleDescriptor("AudioSystem", "AudioSystem", 2, DrawAudioSettings));
            registry.Register(new FrameModuleDescriptor("EventSystem", "EventSystem", 3, DrawEventSystemSettings));
            registry.Register(new FrameModuleDescriptor("Pooling", "Pooling", 4, DrawPoolingSettings));
            registry.Register(new FrameModuleDescriptor("ResSystem", "ResSystem", 5, DrawResSystemSettings));
            registry.Register(new FrameModuleDescriptor("UISystem", "UISystem", 6, DrawUISystemSettings));
            registry.Register(new FrameModuleDescriptor("ConfigInstaller", "ConfigInstaller", 7, DrawConfigInstallerSettings));

            return registry;
        }
    }
}

