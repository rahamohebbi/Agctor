using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;

using AgctorSDK.Core.ProjectMemory;

using AgctorSDK.Core.ProjectMemory.Visual;

using AgctorSDK.Core.ProjectMemory.Visual.Actors;

using AgctorSDK.Core.Tools;

using AgctorSDK.Core.Tools.Models;



namespace AgctorSDK.Core.Tools.Implementations;



/// <summary>

/// Vision extraction via Gemma 4 / Ollama <c>/api/chat</c> (PRD-023d).

/// </summary>

[AgctorHostTool(

    "person-visual-extract",

    "Person visual extract",

    "Runs or inspects Ollama vision extraction on uploaded photos (Extract, ReExtract, GetExtraction).",

    DefaultOperation = "Extract")]

public sealed class PersonVisualExtractTool : ToolActorBase

{

    public PersonVisualExtractTool(string id) : base(id, nameof(PersonVisualExtractTool))

    {

    }



    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>

        Task.FromResult(new ToolResult

        {

            IsSuccess = false,

            Error = "PersonVisualExtractTool expects a ToolRequest with a supported Operation."

        });



    public override async Task<ToolResult> Handle(ToolRequest request)

    {

        var op = request.Operation?.Trim() ?? "";

        var p = request.Parameters ?? new Dictionary<string, object>();



        try

        {

            var catalog = ProjectMemoryServiceAccessor.GetRequiredService<VisualAssetCatalogStore>();

            var root = VisualToolParams.ResolveProjectRoot(p);

            var scenarioId = VisualToolParams.RequireScenarioId(p);

            var assetId = VisualToolParams.RequireAssetId(p);



            var record = await catalog.LoadAsync(root, scenarioId, assetId, CancellationToken.None).ConfigureAwait(false);

            if (record == null)

                return new ToolResult { IsSuccess = false, Error = "asset_not_found" };



            return op.ToLowerInvariant() switch

            {

                "extract" or "reextract" => await ExtractAsync(p, reExtract: op.Equals("reextract", StringComparison.OrdinalIgnoreCase),

                        CancellationToken.None)

                    .ConfigureAwait(false),

                "getextraction" => new ToolResult

                {

                    IsSuccess = true,

                    Output = VisualToolParams.ToJson(new

                    {

                        assetId = record.AssetId,

                        state = record.State,

                        extraction = record.Extraction

                    })

                },

                _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }

            };

        }

        catch (Exception ex)

        {

            return new ToolResult { IsSuccess = false, Error = ex.Message };

        }

    }



    private static async Task<ToolResult> ExtractAsync(

        IDictionary<string, object> p,

        bool reExtract,

        CancellationToken cancellationToken)

    {

        var pipeline = ProjectMemoryServiceAccessor.GetRequiredService<IVisualPipelineService>();

        var root = VisualToolParams.ResolveProjectRoot(p);

        var scenarioId = VisualToolParams.RequireScenarioId(p);

        var assetId = VisualToolParams.RequireAssetId(p);



        var result = await pipeline.ExtractAsync(new VisualExtractRequest

        {

            ProjectRoot = root,

            ScenarioId = scenarioId,

            AssetId = assetId,

            UserMessage = VisualToolParams.GetString(p, "userMessage"),

            FocusEntityKey = VisualToolParams.GetString(p, "focusEntityKey"),

            ReExtract = reExtract

        }, cancellationToken).ConfigureAwait(false);



        if (!result.Success)

            return new ToolResult { IsSuccess = false, Error = result.Error ?? "extract_failed" };



        return new ToolResult

        {

            IsSuccess = true,

            Output = VisualToolParams.ToJson(new

            {

                assetId,

                status = result.Skipped ? "skipped" : result.Record?.Extraction.Status ?? "completed",

                skipped = result.Skipped,

                model = result.ModelUsed,

                intentCount = result.IntentCount,

                proposalCount = result.ProposalCount,

                routedCount = result.RoutedCount,

                state = result.Record?.State

            })

        };

    }

}


