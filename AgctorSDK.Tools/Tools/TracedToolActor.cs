using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Core.Tools
{
    /// <summary>
    /// Decorator for IToolActor that adds activity tracking capabilities.
    /// This allows tracing tool operations without modifying the core tool implementations.
    /// </summary>
    public class TracedToolActor : IToolActor
    {
        private readonly IToolActor _innerTool;
        private readonly IActivityTracker _activityTracker;
        
        /// <summary>
        /// Initializes a new instance of the TracedToolActor class.
        /// </summary>
        /// <param name="innerTool">The tool being decorated.</param>
        /// <param name="activityTracker">The activity tracker to use for tracing.</param>
        public TracedToolActor(IToolActor innerTool, IActivityTracker activityTracker)
        {
            _innerTool = innerTool;
            _activityTracker = activityTracker;
        }
        
        /// <inheritdoc/>
        public string Id => _innerTool.Id;

        /// <inheritdoc/>
        public string ActorType => _innerTool.ActorType;

        /// <inheritdoc/>
        public ActorState State => _innerTool.State;

        /// <inheritdoc/>
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged
        {
            add => _innerTool.StateChanged += value;
            remove => _innerTool.StateChanged -= value;
        }

        /// <inheritdoc/>
        public Task<ToolResult> Handle(ToolRequest request)
        {
            using var activity = _activityTracker.StartActivity("Tool.Handle");
            
            activity.SetAttribute("tool.id", Id);
            activity.SetAttribute("tool.type", _innerTool.GetType().Name);
            activity.SetAttribute("tool.operation", request.Operation);
            
            foreach (var param in request.Parameters)
            {
                activity.SetAttribute($"tool.param.{param.Key}", param.Value?.ToString() ?? "null");
            }
            
            try
            {
                var resultTask = _innerTool.Handle(request);
                
                // Since we can't use 'await' here (we need to maintain the original signature),
                // we'll do our best effort to provide useful tracing
                
                if (resultTask.IsCompleted)
                {
                    var result = resultTask.Result;
                    activity.SetAttribute("tool.result.success", result.IsSuccess.ToString());
                    if (!result.IsSuccess)
                    {
                        activity.SetAttribute("tool.result.error", result.Error);
                    }
                    
                    activity.SetStatus(result.IsSuccess ? ActivityStatus.Ok : ActivityStatus.Error);
                }
                else
                {
                    // Set a continuation to record the result when it completes
                    resultTask.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            var result = t.Result;
                            activity.SetAttribute("tool.result.success", result.IsSuccess.ToString());
                            if (!result.IsSuccess)
                            {
                                activity.SetAttribute("tool.result.error", result.Error);
                            }
                            
                            activity.SetStatus(result.IsSuccess ? ActivityStatus.Ok : ActivityStatus.Error);
                        }
                        else
                        {
                            activity.SetStatus(ActivityStatus.Error);
                            if (t.Exception != null)
                            {
                                activity.RecordException(t.Exception);
                            }
                        }
                    });
                }
                
                return resultTask;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity(
                $"Tool.ReceiveAsync",
                envelope.ExtractActivityContext() // Extract parent context if present
            );
            
            activity.SetAttribute("tool.id", Id);
            activity.SetAttribute("tool.type", _innerTool.GetType().Name);
            activity.SetAttribute("message.id", envelope.Id);
            activity.SetAttribute("message.type", envelope.PayloadType());
            
            try
            {
                var result = await _innerTool.ReceiveAsync(envelope, cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
                return result;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Tool.Initialize");
            
            activity.SetAttribute("tool.id", Id);
            activity.SetAttribute("tool.type", _innerTool.GetType().Name);
            
            try
            {
                await _innerTool.InitializeAsync(cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Tool.Shutdown");
            
            activity.SetAttribute("tool.id", Id);
            activity.SetAttribute("tool.type", _innerTool.GetType().Name);
            
            try
            {
                await _innerTool.ShutdownAsync(cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }
    }
} 