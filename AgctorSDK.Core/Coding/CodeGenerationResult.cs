namespace AgctorSDK.Core.Coding
{
    /// <summary>
    /// Result of a code-generation operation. Contains one or more file patches plus a human-readable summary.
    /// </summary>
    public sealed class CodeGenerationResult
    {
        /// <summary>
        /// Gets or sets whether generation succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Optional error message when <see cref="Success"/> is false.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Array of generated file patches. Each element is a tuple (path, content).
        /// </summary>
        public required (string path, string content)[] Patches { get; set; } = [];

        /// <summary>
        /// Optional textual summary of what was generated/modified.
        /// </summary>
        public string? Summary { get; set; }
    }
} 