using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Adapters;
using AgctorRuntimeCatalog = AgctorSDK.Core.Runtime.AgctorRuntimeCatalog;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Factory implementation for creating actor runtime adapters using dependency injection.
    /// Provides runtime selection and adapter instantiation capabilities.
    /// </summary>
    public class ActorRuntimeAdapterFactory : IActorRuntimeAdapterFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AgctorOptions _options;
        
        // Map of runtime names to their corresponding types
        private static readonly Dictionary<string, Type> RuntimeTypeMap = new()
        {
            { "InMemory", typeof(InMemoryActorRuntime) },
            { "Orleans", typeof(OrleansAdapter) },
            { "Proto.Actor", typeof(ProtoActorAdapter) }
        };

        /// <summary>
        /// Initializes a new instance of the ActorRuntimeAdapterFactory.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection</param>
        /// <param name="options">Configuration options for the actor system</param>
        public ActorRuntimeAdapterFactory(IServiceProvider serviceProvider, IOptions<AgctorOptions> options)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options?.Value ?? new AgctorOptions();
        }

        /// <summary>
        /// Gets a list of all available runtime adapter names.
        /// </summary>
        /// <returns>Collection of available runtime names</returns>
        public IEnumerable<string> GetAvailableRuntimes()
        {
            return RuntimeTypeMap.Keys.ToList();
        }

        /// <summary>
        /// Creates an instance of the specified runtime adapter using dependency injection.
        /// </summary>
        /// <param name="runtimeName">Name of the runtime to create</param>
        /// <returns>An instance of the requested runtime adapter</returns>
        /// <exception cref="ArgumentException">Thrown when the runtime name is not recognized</exception>
        /// <exception cref="InvalidOperationException">Thrown when the runtime cannot be created</exception>
        public IActorRuntimeAdapter CreateRuntime(string runtimeName)
        {
            if (string.IsNullOrWhiteSpace(runtimeName))
            {
                throw new ArgumentException("Runtime name cannot be null or empty", nameof(runtimeName));
            }

            if (!RuntimeTypeMap.TryGetValue(runtimeName, out var runtimeType))
            {
                var availableRuntimes = string.Join(", ", GetAvailableRuntimes());
                throw new ArgumentException(
                    $"Unknown runtime '{runtimeName}'. Available runtimes: {availableRuntimes}", 
                    nameof(runtimeName));
            }

            try
            {
                // Use the service provider to create the runtime instance
                var runtime = _serviceProvider.GetRequiredService(runtimeType) as IActorRuntimeAdapter;
                
                if (runtime == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to create runtime '{runtimeName}'. The service provider returned null or an incompatible type.");
                }

                return runtime;
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new InvalidOperationException(
                    $"Failed to create runtime '{runtimeName}'. Ensure the runtime is properly registered in the DI container.", 
                    ex);
            }
        }

        /// <summary>
        /// Creates an instance of the specified runtime adapter with generic type parameter.
        /// </summary>
        /// <typeparam name="T">The type of runtime adapter to create</typeparam>
        /// <returns>An instance of the requested runtime adapter</returns>
        /// <exception cref="InvalidOperationException">Thrown when the runtime cannot be created</exception>
        public T CreateRuntime<T>() where T : class, IActorRuntimeAdapter
        {
            try
            {
                var runtime = _serviceProvider.GetRequiredService<T>();
                
                if (runtime == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to create runtime of type '{typeof(T).Name}'. The service provider returned null.");
                }

                return runtime;
            }
            catch (Exception ex) when (!(ex is InvalidOperationException && ex.Message.Contains("Failed to create runtime")))
            {
                throw new InvalidOperationException(
                    $"Failed to create runtime of type '{typeof(T).Name}'. Ensure the runtime is properly registered in the DI container.", 
                    ex);
            }
        }

        /// <summary>
        /// Checks if a runtime adapter with the specified name is available.
        /// </summary>
        /// <param name="runtimeName">Name of the runtime to check</param>
        /// <returns>True if the runtime is available, false otherwise</returns>
        public bool IsRuntimeAvailable(string runtimeName)
        {
            if (string.IsNullOrWhiteSpace(runtimeName))
            {
                return false;
            }

            if (!RuntimeTypeMap.TryGetValue(runtimeName, out var runtimeType))
                return false;

            if (AgctorRuntimeCatalog.IsExperimental(runtimeName) && !_options.AllowExperimentalRuntimes)
                return false;

            // Check if the runtime type is registered in the DI container
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetService(runtimeType);
                return service != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the default runtime adapter name from configuration.
        /// </summary>
        /// <returns>The name of the default runtime adapter</returns>
        public string GetDefaultRuntimeName()
        {
            var defaultRuntime = _options.DefaultRuntime;
            
            // Validate that the default runtime is available
            if (!RuntimeTypeMap.ContainsKey(defaultRuntime))
            {
                // Fall back to InMemory if the configured default is not available
                return "InMemory";
            }

            return defaultRuntime;
        }

        /// <summary>
        /// Creates the default runtime adapter based on configuration.
        /// </summary>
        /// <returns>An instance of the default runtime adapter</returns>
        public IActorRuntimeAdapter CreateDefaultRuntime()
        {
            var defaultRuntimeName = GetDefaultRuntimeName();
            return CreateRuntime(defaultRuntimeName);
        }
    }
} 