using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.LifeSignals;

namespace AgctorSDK.Core.ProjectMemory.Companion;

/// <summary>Actor facade for read-only people life-signal scans.</summary>
public interface IProactiveSignalsService
{
    Task<IReadOnlyList<PersonLifeSignal>> ScanAsync(
        string projectRoot,
        string? scenarioId,
        int staleContactDays = 30,
        int birthdayHorizonDays = 14,
        CancellationToken cancellationToken = default);
}
