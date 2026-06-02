namespace Library.Services;

public static class KeepScreenOnService
{
    public static void SetEnabled(bool enabled)
    {
#if IOS
        try
        {
            UIKit.UIApplication.SharedApplication.IdleTimerDisabled = enabled;
        }
        catch
        {
            // Best effort only. Ignore platform/runtime failures.
        }
#elif ANDROID
        try
        {
            Library.Platforms.Android.MainActivity.SetKeepScreenOn(enabled);
        }
        catch
        {
            // Best effort only. Ignore platform/runtime failures.
        }
#else
        _ = enabled;
#endif
    }
}
