using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Traces;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers
{
    /// <summary>
    /// Provides lightweight visualization endpoints backed by OpenTelemetry/activity data.
    /// Used by the dashboard to render per-message traces.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class VisualizationController : ControllerBase
    {
        private readonly IVisualizationService _visualizationService;
        private readonly ISessionStore? _sessionStore;
        private readonly ITraceTimelineStore? _traceTimelineStore;
        private readonly IActivityTracker? _activityTracker;

        public VisualizationController(
            IVisualizationService visualizationService,
            ISessionStore? sessionStore = null,
            ITraceTimelineStore? traceTimelineStore = null,
            IActivityTracker? activityTracker = null)
        {
            _visualizationService = visualizationService ?? throw new ArgumentNullException(nameof(visualizationService));
            _sessionStore = sessionStore;
            _traceTimelineStore = traceTimelineStore;
            _activityTracker = activityTracker;
        }

        /// <summary>
        /// Returns a Mermaid sequence diagram for the given trace identifier.
        /// </summary>
        /// <param name="traceId">Trace identifier (typically from OpenTelemetry)</param>
        [HttpGet("trace/{traceId}/message-flow")]
        [ProducesResponseType(typeof(TraceVisualizationResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<TraceVisualizationResponse>> GetMessageFlowAsync(
            [FromRoute] string traceId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return BadRequest("traceId is required.");
            }

            // Generate Mermaid diagram text from the trace.
            // When no activity data is available, this returns a placeholder diagram.
            var mermaid = await _visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);
            var externalUrl = _visualizationService.GetTraceViewerUrl(traceId);

            var dto = new TraceVisualizationResponse
            {
                TraceId = traceId,
                Mermaid = mermaid,
                ExternalViewerUrl = externalUrl
            };

            return Ok(dto);
        }

        /// <summary>
        /// Returns a structured timeline for the given trace identifier.
        /// </summary>
        [HttpGet("trace/{traceId}/timeline")]
        [ProducesResponseType(typeof(TraceTimelineResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<TraceTimelineResponse>> GetTimelineAsync(
            [FromRoute] string traceId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return BadRequest("traceId is required.");
            }

            if (_traceTimelineStore != null)
            {
                var stored = await _traceTimelineStore.GetAsync(traceId, cancellationToken);
                if (stored != null)
                {
                    if (string.IsNullOrWhiteSpace(stored.ExternalViewerUrl))
                    {
                        stored.ExternalViewerUrl = _visualizationService.GetTraceViewerUrl(traceId);
                    }

                    return Ok(stored);
                }
            }

            var activities = _activityTracker == null
                ? Array.Empty<IActivity>()
                : (await _activityTracker.GetTraceActivitiesAsync(traceId)).ToArray();

            var ordered = activities
                .OrderBy(a => a.Timestamp)
                .ThenBy(a => a.ParentId == null ? 0 : 1)
                .ToList();

            var response = new TraceTimelineResponse
            {
                TraceId = traceId,
                ExternalViewerUrl = _visualizationService.GetTraceViewerUrl(traceId)
            };

            if (ordered.Count == 0)
            {
                return Ok(response);
            }

            var start = ordered.Min(a => a.Timestamp);
            var end = ordered.Max(a => a.Timestamp.Add(a.Duration));
            var depthMap = BuildDepthMap(ordered);

            response.StartedAtUtc = start;
            response.TotalDurationMs = Math.Max(1, (end - start).TotalMilliseconds);
            response.Events = ordered
                .Select((activity, index) => TraceTimelineEventMapper.Map(activity, index + 1, start, depthMap))
                .ToList();

            return Ok(response);
        }

        /// <summary>
        /// Resolves a historical timeline by a specific message turn identifier.
        /// </summary>
        [HttpGet("sessions/{sessionId}/messages/{turnId}/timeline")]
        [ProducesResponseType(typeof(TraceTimelineResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TraceTimelineResponse>> GetTimelineByMessageAsync(
            [FromRoute] string sessionId,
            [FromRoute] string turnId,
            CancellationToken cancellationToken = default)
        {
            if (_sessionStore == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "TRACE_LOOKUP_UNAVAILABLE",
                    Message = "Trace lookup is not available."
                });
            }

            var link = await _sessionStore.GetTraceLinkByTurnIdAsync(sessionId, turnId, cancellationToken);
            if (link == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "TRACE_LINK_NOT_FOUND",
                    Message = $"No trace link was found for turn '{turnId}'."
                });
            }

            var resolvedTraceId = ResolveTraceIdForTurn(link, turnId);
            if (string.IsNullOrWhiteSpace(resolvedTraceId))
            {
                return Ok(new TraceTimelineResponse());
            }

            return await GetTimelineAsync(resolvedTraceId, cancellationToken);
        }

        /// <summary>
        /// Resolves a historical timeline by the logical turn group.
        /// </summary>
        [HttpGet("sessions/{sessionId}/turns/{turnId}/timeline")]
        [ProducesResponseType(typeof(TraceTimelineResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TraceTimelineResponse>> GetTimelineByTurnAsync(
            [FromRoute] string sessionId,
            [FromRoute] string turnId,
            CancellationToken cancellationToken = default)
        {
            if (_sessionStore == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "TRACE_LOOKUP_UNAVAILABLE",
                    Message = "Trace lookup is not available."
                });
            }

            var link = await _sessionStore.GetTraceLinkByTurnIdAsync(sessionId, turnId, cancellationToken);
            if (link == null || string.IsNullOrWhiteSpace(link.PrimaryTraceId))
            {
                return NotFound(new ErrorResponse
                {
                    Code = "TRACE_LINK_NOT_FOUND",
                    Message = $"No turn-level trace was found for turn '{turnId}'."
                });
            }

            return await GetTimelineAsync(link.PrimaryTraceId, cancellationToken);
        }

        private static Dictionary<string, int> BuildDepthMap(IReadOnlyCollection<IActivity> activities)
        {
            var activityMap = activities.ToDictionary(activity => activity.Id);
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var activity in activities)
            {
                depths[activity.Id] = GetDepth(activity, activityMap, depths);
            }

            return depths;
        }

        private static int GetDepth(
            IActivity activity,
            IReadOnlyDictionary<string, IActivity> activityMap,
            IDictionary<string, int> cache)
        {
            if (cache.TryGetValue(activity.Id, out var cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(activity.ParentId) || !activityMap.TryGetValue(activity.ParentId, out var parent))
            {
                return 0;
            }

            return GetDepth(parent, activityMap, cache) + 1;
        }

        private static string? ResolveTraceIdForTurn(AgctorSDK.Core.Sessions.Models.SessionTraceLink link, string turnId)
        {
            if (string.Equals(link.RequestTurnId, turnId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(link.RequestTraceId))
            {
                return link.RequestTraceId;
            }

            if (string.Equals(link.ResponseTurnId, turnId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(link.ResponseTraceId))
            {
                return link.ResponseTraceId;
            }

            return link.PrimaryTraceId;
        }
    }
}

