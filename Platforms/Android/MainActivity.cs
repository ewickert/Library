#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace Library.Platforms.Android;

[Activity(
    Label = "MTG Library",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private static MainActivity? _current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = this;
    }

    protected override void OnResume()
    {
        base.OnResume();
        _current = this;
    }

    public static void SetKeepScreenOn(bool enabled)
    {
        var activity = _current;
        if (activity?.Window == null)
            return;

        activity.RunOnUiThread(() =>
        {
            if (enabled)
                activity.Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            else
                activity.Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        });
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    => Program.ConfigureAvaloniaApp(builder);
}
#endif
