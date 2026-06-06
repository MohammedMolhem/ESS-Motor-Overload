using System;
using System.IO;

namespace EES_MotorOverload_V1
{
    public static class Logger
    {
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EES_MotorOverload_V1", "Logs");

        private static readonly object _lockObject = new object();

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(_logPath);
            }
            catch { }
        }

        public static void Info(string message) => Log("INFO", message, null);
        public static void Warn(string message) => Log("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Log("ERROR", message, ex);
        public static void Debug(string message) => Log("DEBUG", message, null);

        private static void Log(string level, string message, Exception ex)
        {
            try
            {
                lock (_lockObject)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logMessage = $"[{timestamp}] [{level,-5}] {message}";
                    if (ex != null)
                        logMessage += $"\n{ex}";

                    System.Diagnostics.Debug.WriteLine(logMessage);

                    string logFile = Path.Combine(_logPath, $"{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFile, logMessage + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}