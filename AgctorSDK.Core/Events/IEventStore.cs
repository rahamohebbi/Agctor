using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Events
{
    /// <summary>
    /// Defines the contract for an event store, which is responsible for recording and querying event data.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>
        /// Records an event asynchronously.
        /// </summary>
        /// <param name="record">The event record to store.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task RecordAsync(EventRecord record);

        /// <summary>
        /// Queries events asynchronously based on a related prompt hash.
        /// </summary>
        /// <param name="promptHash">The hash of the prompt to find related events for.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a collection of event records.</returns>
        Task<IEnumerable<EventRecord>> QueryAsync(string promptHash);
    }
} 