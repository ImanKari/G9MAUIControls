using ObjCRuntime;
using UIKit;

namespace G9Controls.Gallery;

public static class Program
{
    // The iOS entry point. UIApplication.Main hands control to AppDelegate.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
