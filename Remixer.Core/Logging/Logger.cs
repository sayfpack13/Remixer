using System;
using System.IO;
using System.Text;

namespace Remixer.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public static class Logger
{
    private static readonly object _lockObject = new();
    private static string? _logFilePath;
    private static LogLevel _minimumLogLevel = LogLevel.Info;

    static Logger()
    {
        InitializeLogger();
    }

    private static void InitializeLogger()
    {
        try
        {
            // Create logs directory in user's AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataPath, "AudioRemixer", "Logs");
            Directory.CreateDirectory(logDirectory);

            // Create log file with timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = Path.Combine(logDirectory, $"remixer_{timestamp}.log");

            // Write initial log entry
            WriteToFile($"=== Audio Remixer Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            WriteToFile($"Log Level: {_minimumLogLevel}");
            WriteToFile($"OS: {Environment.OSVersion}");
            WriteToFile($".NET Version: {Environment.Version}");
            WriteToFile("");
        }
        catch (Exception ex)
        {
            // If we can't initialize logging, at least write to debug output
            System.Diagnostics.Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
        }
    }

    public static void SetMinimumLogLevel(LogLevel level)
    {
        _minimumLogLevel = level;
        Info($"Log level changed to: {level}");
    }

    public static string? GetLogFilePath() => _logFilePath;

    public static void Debug(string message, Exception? exception = null)
    {
        Log(LogLevel.Debug, message, exception);
    }

    public static void Info(string message, Exception? exception = null)
    {
        Log(LogLevel.Info, message, exception);
    }

    public static void Warning(string message, Exception? exception = null)
    {
        Log(LogLevel.Warning, message, exception);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Log(LogLevel.Error, message, exception);
    }

    public static void Critical(string message, Exception? exception = null)
    {
        Log(LogLevel.Critical, message, exception);
    }

    private static void Log(LogLevel level, string message, Exception? exception)
    {
        if (level < _minimumLogLevel)
            return;

        try
        {
            var logEntry = FormatLogEntry(level, message, exception);
            
            // Write to file
            WriteToFile(logEntry);
            
            // Also write to debug output for development
            System.Diagnostics.Debug.WriteLine(logEntry);
        }
        catch (Exception ex)
        {
            // Last resort - at least try to write to debug output
            System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private static string FormatLogEntry(LogLevel level, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        
        sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ");
        sb.Append($"[{level,-8}] ");
        sb.Append(message);

        if (exception != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {exception.GetType().Name}: {exception.Message}");
            sb.AppendLine();
            sb.Append($"  Stack Trace: {exception.StackTrace}");
            
            if (exception.InnerException != null)
            {
                sb.AppendLine();
                sb.Append($"  Inner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
            }
        }

        return sb.ToString();
    }

    private static void WriteToFile(string message)
    {
        if (string.IsNullOrEmpty(_logFilePath))
            return;

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_logFilePath, message + Environment.NewLine);
            }
            catch
            {
                // Silently fail if we can't write to log file
            }
        }
    }

    public static void LogMethodEntry(string className, string methodName, params object[] parameters)
    {
        var paramString = parameters.Length > 0 
            ? string.Join(", ", parameters) 
            : "no parameters";
        Debug($"[ENTRY] {className}.{methodName}({paramString})");
    }

    public static void LogMethodExit(string className, string methodName, object? result = null)
    {
        var resultString = result != null ? $" -> {result}" : "";
        Debug($"[EXIT] {className}.{methodName}{resultString}");
    }

    public static void LogPerformance(string operation, long milliseconds)
    {
        var level = milliseconds > 1000 ? LogLevel.Warning : LogLevel.Debug;
        Log(level, $"[PERF] {operation} took {milliseconds}ms", null);
    }
}

