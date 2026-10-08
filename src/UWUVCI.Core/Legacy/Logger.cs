using System;
using System.IO;
using UWUVCI.Core;

namespace UWUVCI_AIO_WPF.Helpers
{
    public static class Logger
    {
        private static readonly object gate = new object();
        private static string logFilePath;

        /// <summary>Raised for every line, so a front end can mirror the log.</summary>
        public static event Action<string> Logged;

        private static string LogFile
        {
            get
            {
                if (logFilePath == null)
                {
                    Directory.CreateDirectory(AppPaths.Logs);
                    logFilePath = Path.Combine(AppPaths.Logs, $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                    CleanupOldLogs(7);
                }
                return logFilePath;
            }
        }

        public static string CurrentLogFile => LogFile;

        public static void Log(string message)
        {
            try
            {
                lock (gate)
                    File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // If logging fails, there's nothing more to do
            }
            try { Logged?.Invoke(message); } catch { }
        }

        private static void CleanupOldLogs(int daysToKeep)
        {
            try
            {
                foreach (var file in Directory.GetFiles(AppPaths.Logs, "log_*.txt"))
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.CreationTime < DateTime.Now.AddDays(-daysToKeep))
                        fi.Delete();
                }
            }
            catch (Exception) { }
        }
    }
}
