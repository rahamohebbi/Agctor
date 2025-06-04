using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Utils
{
    /// <summary>
    /// Helper class for executing Git CLI commands.
    /// </summary>
    public static class GitCliHelper
    {
        /// <summary>
        /// Executes a process asynchronously and returns its standard output and standard error.
        /// </summary>
        /// <param name="fileName">The command or application to execute (e.g., "git").</param>
        /// <param name="arguments">The arguments to pass to the command.</param>
        /// <param name="workingDirectory">The working directory for the command.</param>
        /// <returns>A tuple containing standard output, standard error, and exit code.</returns>
        private static async Task<(string StandardOutput, string StandardError, int ExitCode)> ExecuteProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // Standard output and error should be read in UTF-8
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(); // Use a more modern way if available or handle timeout

            return (outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim(), process.ExitCode);
        }

        /// <summary>
        /// Checks if a directory is a Git repository.
        /// </summary>
        /// <param name="directoryPath">The path to the directory.</param>
        /// <returns>True if it is a Git repository, false otherwise.</returns>
        public static async Task<bool> IsGitRepositoryAsync(string directoryPath)
        {
            // `git rev-parse --is-inside-work-tree` is a common way to check
            // It exits with 0 if inside a work tree, non-zero otherwise, and prints true/false to stdout.
            var (stdOut, stdErr, exitCode) = await ExecuteProcessAsync("git", "rev-parse --is-inside-work-tree", directoryPath);
            return exitCode == 0 && stdOut.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Initializes a new Git repository in the specified directory.
        /// </summary>
        /// <param name="directoryPath">The path where the repository should be initialized.</param>
        public static async Task InitAsync(string directoryPath)
        {
            var (stdOut, stdErr, exitCode) = await ExecuteProcessAsync("git", "init", directoryPath);
            if (exitCode != 0)
            {
                throw new System.Exception($"Git init failed: {stdErr} {stdOut}");
            }
        }

        /// <summary>
        /// Stages a specific file in the Git repository.
        /// </summary>
        /// <param name="repositoryPath">The path to the Git repository.</param>
        /// <param name="filePathToAdd">The path of the file to stage (can be relative to the repository root).</param>
        public static async Task AddAsync(string repositoryPath, string filePathToAdd)
        {
            // Ensure filePathToAdd is properly quoted if it contains spaces
            var (stdOut, stdErr, exitCode) = await ExecuteProcessAsync("git", $"add \"{filePathToAdd}\"", repositoryPath);
            if (exitCode != 0)
            {
                throw new System.Exception($"Git add failed for file '{filePathToAdd}': {stdErr} {stdOut}");
            }
        }

        /// <summary>
        /// Commits staged changes to the Git repository.
        /// </summary>
        /// <param name="repositoryPath">The path to the Git repository.</param>
        /// <param name="message">The commit message.</param>
        /// <param name="authorName">The name of the commit author.</param>
        /// <param name="authorEmail">The email of the commit author.</param>
        public static async Task CommitAsync(string repositoryPath, string message, string authorName, string authorEmail)
        {
            // Using -c user.name and -c user.email to override config for this commit only.
            // This makes the commit independent of global/local Git config for user identity.
            var commitArgs = $"-c user.name=\"{authorName}\" -c user.email=\"{authorEmail}\" commit -m \"{message.Replace("\"", "\\\"")}\"";
            var (stdOut, stdErr, exitCode) = await ExecuteProcessAsync("git", commitArgs, repositoryPath);
            if (exitCode != 0)
            {
                // Handle case where there might be nothing to commit (exit code 1 for git commit with nothing staged)
                if (stdOut.Contains("nothing to commit") || stdErr.Contains("nothing to commit"))
                {
                    // This can be treated as a non-error or logged as info
                    System.Console.WriteLine($"Git commit: Nothing to commit in {repositoryPath}.");
                    return; 
                }
                throw new System.Exception($"Git commit failed: {stdErr} {stdOut}");
            }
        }
        
        /// <summary>
        /// Retrieves the hash of the latest commit on the current branch.
        /// </summary>
        /// <param name="repositoryPath">The path to the Git repository.</param>
        /// <returns>The latest commit hash, or null if no commits exist.</returns>
        public static async Task<string?> GetLatestCommitHashAsync(string repositoryPath)
        {
            var (stdOut, stdErr, exitCode) = await ExecuteProcessAsync("git", "rev-parse HEAD", repositoryPath);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdOut))
            {
                return stdOut.Trim();
            }
            // If HEAD doesn't exist (e.g., new repo with no commits), it will exit with non-zero.
            return null; 
        }
    }
} 