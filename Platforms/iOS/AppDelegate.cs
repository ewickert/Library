#if IOS
using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace Library.Platforms.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    => Program.ConfigureAvaloniaApp(builder);
}
#endif
