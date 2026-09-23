using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Completion;

namespace AgctorSDK.Core.Completion
{
    /// <summary>
    /// Tries generators in order and returns the first successful text.
    /// Later generators are the offline path when a model is not running.
    /// </summary>
    public sealed class ChainedTextGenerator : ITextGenerator
    {
        private readonly IReadOnlyList<ITextGenerator> _generators;

        public ChainedTextGenerator(params ITextGenerator[] generators)
        {
            if (generators == null || generators.Length == 0)
            {
                throw new ArgumentException("At least one generator is required.", nameof(generators));
            }

            _generators = generators;
        }

        public string Name => "chain:" + string.Join(",", _generators.Select(generator => generator.Name));

        public async Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken = default)
        {
            string? lastError = null;
            foreach (var generator in _generators)
            {
                var result = await generator.GenerateAsync(request, cancellationToken);
                if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Text))
                {
                    return result;
                }

                lastError = result.Error ?? lastError;
            }

            return new TextGenerationResult(false, string.Empty, Name, lastError ?? "Every generator failed.");
        }
    }
}
