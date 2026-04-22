using System.Diagnostics;
using System.Text;

namespace PhotoView.Helpers;

public static class AppDiagnostics
{
    public static void Info(string area, string message)
    {
        Write("INFO", area, message);
    }

    public static void Warn(string area, string message)
    {
        Write("WARN", area, message);
    }

    public static void Error(string area, string message, Exception? exception = null, params (string Key, object? Value)[] context)
    {
        var builder = new StringBuilder(message);
        foreach (var (key, value) in context)
        {
            builder.Append(" | ");
            builder.Append(key);
            builder.Append('=');
            builder.Append(value ?? "<null>");
        }

        if (exception != null)
        {
            builder.Append(" | ");
            builder.Append(exception.GetType().Name);
            builder.Append(": ");
            builder.Append(exception.Message);
        }

        Write("ERROR", area, builder.ToString());
    }

    public static bool IsExpectedCancellation(Exception exception)
    {
        return exception is OperationCanceledException || exception.InnerException is OperationCanceledException;
    }

    private static void Write(string level, string area, string message)
    {
        Debug.WriteLine($"[{level}] [{area}] {message}");
    }
}
