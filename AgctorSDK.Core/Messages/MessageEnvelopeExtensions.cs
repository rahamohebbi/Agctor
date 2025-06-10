using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Extension methods for IMessageEnvelope to support activity tracking.
    /// </summary>
    public static class MessageEnvelopeExtensions
    {
        /// <summary>
        /// Propagates the current activity context to the message envelope.
        /// This should be used when sending a message from a parent to child agent
        /// to maintain the trace context.
        /// </summary>
        /// <param name="envelope">The message envelope to add context to.</param>
        /// <param name="activityTracker">The activity tracker containing the current context.</param>
        /// <returns>A new message envelope with the propagated activity context headers.</returns>
        public static IMessageEnvelope PropagateActivityContext(
            this IMessageEnvelope envelope, 
            IActivityTracker activityTracker)
        {
            // Extract the current context
            var activityContext = activityTracker.ExtractContext();
            
            // Create a new dictionary with all existing headers plus new context headers
            var newHeaders = new Dictionary<string, string>();
            
            // Copy existing headers
            foreach (var header in envelope.Headers)
            {
                newHeaders[header.Key] = header.Value;
            }
            
            // Add activity context headers
            foreach (var item in activityContext)
            {
                newHeaders[item.Key] = item.Value;
            }
            
            // Return a new envelope with the updated headers
            return envelope.WithHeaders(newHeaders);
        }
        
        /// <summary>
        /// Extracts the activity context from the message envelope.
        /// This can be used when receiving a message to extract the parent context.
        /// </summary>
        /// <param name="envelope">The message envelope containing the context.</param>
        /// <returns>A dictionary containing the activity context.</returns>
        public static IReadOnlyDictionary<string, string> ExtractActivityContext(
            this IMessageEnvelope envelope)
        {
            // Just return the headers directly since they're already IReadOnlyDictionary
            return envelope.Headers;
        }
        
        /// <summary>
        /// Gets the type name of the payload.
        /// </summary>
        /// <param name="envelope">The message envelope.</param>
        /// <returns>The type name of the payload, or "Unknown" if the payload is null.</returns>
        public static string PayloadType(this IMessageEnvelope envelope)
        {
            return envelope.Payload?.GetType().Name ?? "Unknown";
        }
    }
} 