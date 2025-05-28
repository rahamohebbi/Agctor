using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Factory interface for creating actor runtime adapters.
    /// Enables dynamic runtime selection and hot-swapping of actor backends.
    /// </summary>
    public interface IActorRuntimeAdapterFactory
    {
        /// <summary>
        /// Gets a list of all available runtime adapter names.
        /// </summary>
        /// <returns>Collection of available runtime names</returns>
        IEnumerable<string> GetAvailableRuntimes();

        /// <summary>
        /// Creates an instance of the specified runtime adapter.
        /// </summary>
        /// <param name="runtimeName">Name of the runtime to create (e.g., "InMemory", "Orleans", "Proto.Actor")</param>
        /// <returns>An instance of the requested runtime adapter</returns>
        /// <exception cref="ArgumentException">Thrown when the runtime name is not recognized</exception>
        /// <exception cref="InvalidOperationException">Thrown when the runtime cannot be created</exception>
        IActorRuntimeAdapter CreateRuntime(string runtimeName);

        /// <summary>
        /// Creates an instance of the specified runtime adapter with generic type parameter.
        /// </summary>
        /// <typeparam name="T">The type of runtime adapter to create</typeparam>
        /// <returns>An instance of the requested runtime adapter</returns>
        /// <exception cref="InvalidOperationException">Thrown when the runtime cannot be created</exception>
        T CreateRuntime<T>() where T : class, IActorRuntimeAdapter;

        /// <summary>
        /// Checks if a runtime adapter with the specified name is available.
        /// </summary>
        /// <param name="runtimeName">Name of the runtime to check</param>
        /// <returns>True if the runtime is available, false otherwise</returns>
        bool IsRuntimeAvailable(string runtimeName);

        /// <summary>
        /// Gets the default runtime adapter name.
        /// </summary>
        /// <returns>The name of the default runtime adapter</returns>
        string GetDefaultRuntimeName();
    }
} 