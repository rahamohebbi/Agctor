using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Options for configuring registered agent types.
    /// </summary>
    public class AgentTypeOptions
    {
        private readonly Dictionary<string, Type> _agentTypes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the registered agent types.
        /// </summary>
        public IReadOnlyDictionary<string, Type> AgentTypes => _agentTypes;

        /// <summary>
        /// Registers an agent type with a type name.
        /// </summary>
        /// <param name="typeName">The type name to register.</param>
        /// <param name="agentType">The agent type to register.</param>
        public void RegisterAgentType(string typeName, Type agentType)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("Type name cannot be null or empty", nameof(typeName));
            }

            if (agentType == null)
            {
                throw new ArgumentNullException(nameof(agentType));
            }

            _agentTypes[typeName] = agentType;
        }

        /// <summary>
        /// Gets the agent type for a type name.
        /// </summary>
        /// <param name="typeName">The type name to look up.</param>
        /// <returns>The agent type, or null if not found.</returns>
        public Type? GetAgentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("Type name cannot be null or empty", nameof(typeName));
            }

            return _agentTypes.TryGetValue(typeName, out var agentType) ? agentType : null;
        }
    }
} 