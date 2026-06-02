using Avalonia;
using System;

namespace Library;

class Program
{
#if DESKTOP
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildDesktopAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
#endif

    public static AppBuilder ConfigureAvaloniaApp(AppBuilder builder)
    {
    #if DESKTOP
        builder = builder.WithInterFont();
    #endif

        return builder.LogToTrace();
    }

#if DESKTOP
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildDesktopAvaloniaApp()
        => ConfigureAvaloniaApp(AppBuilder.Configure<App>().UsePlatformDetect());
#endif
}
