using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Builds a choice request. Actors pass the result to <see cref="IActorContext.Decide"/>.
    /// </summary>
    public static class Choice
    {
        public static DecisionRequest Create(string question, params string[] options)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            return new DecisionRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = DecisionType.Choice,
                Question = question,
                Options = (options ?? Array.Empty<string>())
                    .Select(option => new DecisionOption(option))
                    .ToList()
            };
        }
    }

    /// <summary>
    /// Builds a score request bounded by <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    public static class Score
    {
        public static DecisionRequest Create(string question, double min = 0, double max = 100)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            return new DecisionRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = DecisionType.Score,
                Question = question,
                Constraints = new DecisionConstraints { Min = min, Max = max }
            };
        }
    }

    /// <summary>
    /// Builds a yes/no request.
    /// </summary>
    public static class Binary
    {
        public static DecisionRequest Create(string question)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            return new DecisionRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = DecisionType.Binary,
                Question = question
            };
        }
    }
}
