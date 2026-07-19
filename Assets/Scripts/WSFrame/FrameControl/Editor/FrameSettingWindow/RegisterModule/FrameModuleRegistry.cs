using System;
using System.Collections.Generic;
using System.Linq;

namespace WS_Modules
{
    internal sealed class FrameModuleRegistry
    {
        private readonly Dictionary<string, FrameModuleDescriptor> _modules = new Dictionary<string, FrameModuleDescriptor>();

        public void Register(FrameModuleDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (string.IsNullOrEmpty(descriptor.Id))
            {
                throw new ArgumentException("Module id cannot be null or empty.", nameof(descriptor));
            }

            _modules[descriptor.Id] = descriptor;
        }

        public bool TryGet(string moduleId, out FrameModuleDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(moduleId))
            {
                descriptor = null;
                return false;
            }

            return _modules.TryGetValue(moduleId, out descriptor);
        }

        public List<FrameModuleDescriptor> GetAll()
        {
            return _modules.Values
                .OrderBy(module => module.Order)
                .ThenBy(module => module.DisplayName)
                .ToList();
        }
    }
}

