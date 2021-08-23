using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsPrometheusSync
{
    /// <summary>
    ///     Basic info of a node needed for the sync process
    /// </summary>
    public class NodeInfo
    {
        private readonly int _hashCode;

        private readonly string _stringValue;

        internal NodeInfo(string name, IDictionary<string, string> labels)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Can not be null or empty", nameof(name));

            Name = name;
            Labels = labels as IReadOnlyDictionary<string, string>;

            // precalculate so we only need to do this once
            _stringValue = $"{Name}";

            int labelsHash;
            unchecked // Overflow is fine, just wrap
            {
                labelsHash = (int)2166136261;
                if (labels?.Any() == true)
                    foreach (var label in labels)
                        labelsHash = (labelsHash * 16777619) ^ (label.Key, label.Value).GetHashCode();
            }

            _hashCode = (Name, labelsHash).GetHashCode();
        }

        /// <summary>
        ///     Name of node
        /// </summary>
        /// <example>aksd1000000</example>
        public string Name { get; }

        /// <summary>
        ///     List of the labels currently on the node
        /// </summary>
        public IReadOnlyDictionary<string, string> Labels { get; }

        public override string ToString()
        {
            return _stringValue;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            return obj is NodeInfo value && value.GetHashCode() == GetHashCode();
        }
    }
}