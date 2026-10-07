using System.Diagnostics;

namespace scada_demo_test.Maui;

public static class MauiDiag
{
    private const string DefaultTag = "MAUI_DIAG";

    public static void Info(string message) => Info(DefaultTag, message);

    public static void Info(string tag, string message)
    {
#if ANDROID
        Android.Util.Log.Info(tag, message ?? "(null)");
#endif
        Debug.WriteLine($"[{tag}] INFO: {message}");
    }

    public static void Warn(string message) => Warn(DefaultTag, message);

    public static void Warn(string tag, string message)
    {
#if ANDROID
        Android.Util.Log.Warn(tag, message ?? "(null)");
#endif
        Debug.WriteLine($"[{tag}] WARN: {message}");
    }

    public static void Error(string message) => Error(DefaultTag, message);

    public static void Error(string tag, string message)
    {
#if ANDROID
        Android.Util.Log.Error(tag, message ?? "(null)");
#endif
        Debug.WriteLine($"[{tag}] ERROR: {message}");
    }

    public static void Error(string tag, System.Exception ex)
    {
        var msg = ex?.ToString() ?? "(null exception)";
        Error(tag, msg);
    }
}