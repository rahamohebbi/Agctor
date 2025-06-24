using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools.Abstractions;

namespace AgctorSDK.Core.Tools.Implementations.Format
{
    internal sealed class PythonFormatter : ICodeFormatter
    {
        private static bool? _isAvailable;
        public string Language => "python";

        public bool IsAvailable => _isAvailable ??= ProbeAvailability();

        private static bool ProbeAvailability()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "black",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc!.WaitForExit(3000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<(bool IsSuccess, string? FormattedCode, string? Error)> FormatAsync(string code, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
            {
                return (false, null, "Python formatter 'black' is not installed. Run: pip install black");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "black",
                    Arguments = "-q -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
                proc.Start();

                await proc.StandardInput.WriteAsync(code);
                proc.StandardInput.Close();

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                await Task.WhenAll(outputTask, errorTask);

                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    var err = await errorTask;
                    return (false, null, $"black exited with code {proc.ExitCode}: {err}");
                }

                var formatted = await outputTask;
                return (true, formatted, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }
} 