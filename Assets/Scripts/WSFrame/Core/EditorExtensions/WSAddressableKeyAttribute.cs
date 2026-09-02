using System;
using UnityEngine;

namespace WS_Modules
{
    /// <summary>
    /// Marks a string or string collection field as an Addressables address selector.
    /// The first filter is a group expression separated by '|'; remaining filters are
    /// label expressions where '&amp;' means AND and '|' means OR.
    /// Multiple label filter arguments are also combined as OR alternatives.
    /// For example, "GroupA|GroupB" selects either group and "Item&amp;Rare|Equipment"
    /// selects entries with both Item and Rare, or with Equipment.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class WSAddressableKeyAttribute : PropertyAttribute
    {
        /// <summary>
        /// Initializes an Addressables selector with a group expression and optional label expressions.
        /// </summary>
        /// <param name="filters">The first value is the group expression; following values are label expressions.</param>
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

        /// <summary>
        /// Gets the raw group expression. Group names separated by '|' are alternatives.
        /// </summary>
        public string GroupName { get; }

        /// <summary>
        /// Gets the raw label expressions. '&amp;' joins required labels and '|' joins alternatives.
        /// </summary>
        public string[] Labels { get; }
    }
}
