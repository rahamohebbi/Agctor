using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageTestRunners
{
    /// <summary>
    /// Executes .NET tests by invoking <c>dotnet test</c> for the specified project or solution.
    /// </summary>
    public class CSharpTestRunner : ILanguageTestRunner
    {
        public string Language => "csharp";

        public async Task<(bool Success, string Output, string Error)> RunTestsAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (false, string.Empty, "Test path is empty.");
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList = { "test", path, "--nologo", "--no-build" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };

                proc.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null) outputBuilder.AppendLine(args.Data);
                };
                proc.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null) errorBuilder.AppendLine(args.Data);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync();

                bool success = proc.ExitCode == 0;
                return (success, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (System.Exception ex)
            {
                errorBuilder.AppendLine(ex.Message);
                return (false, outputBuilder.ToString(), errorBuilder.ToString());
            }
        }
    }
} 