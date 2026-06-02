#if IOS
using UIKit;

namespace Library.Platforms.iOS;

public static class MainClass
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
#endif
