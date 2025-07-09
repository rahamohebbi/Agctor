using System;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection
{
    public static class TaskStoreServiceExtensions
    {
        public static IServiceCollection AddInMemoryTaskStore(this IServiceCollection services, string? filePath = null)
        {
            services.AddSingleton<ITaskStore>(_ => new InMemoryTaskStore(filePath));
            return services;
        }
    }
} 