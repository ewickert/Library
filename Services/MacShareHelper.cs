#if DESKTOP
using System.Runtime.InteropServices;

namespace Library.Services;

/// <summary>
/// Shows the macOS NSSharingServicePicker for a saved file path.
/// Silently no-ops on non-macOS or if the window handle is unavailable.
/// </summary>
internal static class MacShareHelper
{
    // Objective-C runtime P/Invoke
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_getUid")]
    private static extern IntPtr SelGetUid([MarshalAs(UnmanagedType.LPStr)] string name);

    // General-purpose send (returns IntPtr)
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr self, IntPtr sel);

    // Send with one IntPtr argument
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendPtr(IntPtr self, IntPtr sel, IntPtr arg0);

    // showRelativeToRect:ofView:preferredEdge: — void return, NSRect + IntPtr + nint args
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void MsgSendShowPicker(IntPtr self, IntPtr sel, NSRect rect, IntPtr view, nint edge);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X, Y, Width, Height;
    }

    public static void ShareFile(string filePath, IntPtr nsWindowHandle)
    {
        if (!OperatingSystem.IsMacOS() || nsWindowHandle == IntPtr.Zero) return;

        try
        {
            // NSString *nsPath = [NSString stringWithUTF8String:filePath]
            var bytes = System.Text.Encoding.UTF8.GetBytes(filePath + "\0");
            var gch = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr nsPath;
            try
            {
                nsPath = MsgSendPtr(ObjcGetClass("NSString"),
                                    SelGetUid("stringWithUTF8String:"),
                                    gch.AddrOfPinnedObject());
            }
            finally { gch.Free(); }

            // NSURL *nsUrl = [NSURL fileURLWithPath:nsPath]
            var nsUrl = MsgSendPtr(ObjcGetClass("NSURL"),
                                   SelGetUid("fileURLWithPath:"),
                                   nsPath);

            // NSArray *items = [NSArray arrayWithObject:nsUrl]
            var items = MsgSendPtr(ObjcGetClass("NSArray"),
                                   SelGetUid("arrayWithObject:"),
                                   nsUrl);

            // NSSharingServicePicker *picker = [[NSSharingServicePicker alloc] initWithItems:items]
            var picker = MsgSend(ObjcGetClass("NSSharingServicePicker"), SelGetUid("alloc"));
            picker = MsgSendPtr(picker, SelGetUid("initWithItems:"), items);

            // NSView *contentView = [nsWindow contentView]
            var contentView = MsgSend(nsWindowHandle, SelGetUid("contentView"));

            // [picker showRelativeToRect:NSZeroRect ofView:contentView preferredEdge:NSMaxYEdge(3)]
            MsgSendShowPicker(picker, SelGetUid("showRelativeToRect:ofView:preferredEdge:"),
                              new NSRect(), contentView, 3);
        }
        catch
        {
            // Silently ignore failures — sharing is best-effort
        }
    }
}
#endif
