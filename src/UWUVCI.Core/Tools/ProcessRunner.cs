using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    public sealed class ProcessResult
    {
        public int ExitCode;
        public string Output = "";
        public string Error = "";
        public bool Ok => ExitCode == 0;
    }

    public static class ProcessRunner
    {
        /// <summary>Runs a tool to completion, capturing its output. Arguments are passed verbatim (no shell quoting).</summary>
        public static ProcessResult Run(string fileName, IEnumerable<string> arguments, string workingDirectory = null, bool log = true)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (workingDirectory != null)
                psi.WorkingDirectory = workingDirectory;
            foreach (string arg in arguments)
                psi.ArgumentList.Add(arg);

            if (log)
                Logger.Log($"Running: {fileName} {string.Join(" ", psi.ArgumentList)}");

            using var process = new Process { StartInfo = psi };
            process.Start();
            // No tool here is interactive; closing stdin makes any "press enter" prompt return immediately.
            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            var result = new ProcessResult { ExitCode = process.ExitCode, Output = stdout.Result, Error = stderr.Result };
            if (log && !result.Ok)
                Logger.Log($"{fileName} exited with {result.ExitCode}: {Tail(result.Error.Length > 0 ? result.Error : result.Output)}");
            return result;
        }

        private static string Tail(string text) => text.Length <= 2000 ? text : text.Substring(text.Length - 2000);
    }
}
