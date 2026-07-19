using System;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// Marks a string or string collection field as an Addressables address selector.
    /// The first filter is the group name; remaining filters are labels that must all match.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class WSAddressableKeyAttribute : PropertyAttribute
    {
        public WSAddressableKeyAttribute(params string[] filters)
        {
            if (filters == null || filters.Length == 0)
            {
                GroupName = string.Empty;
                Labels = Array.Empty<string>();
                return;
            }

            GroupName = filters[0];

            int labelCount = Math.Max(0, filters.Length - 1);
            Labels = new string[labelCount];
            for (int i = 0; i < labelCount; i++)
            {
                Labels[i] = filters[i + 1];
            }
        }

        public string GroupName { get; }

        public string[] Labels { get; }
    }
}
