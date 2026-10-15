using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Flipclock.App;

internal static partial class Native
{
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;
    public const int SC_MINIMIZE = 0xF020;
    public const int HTCAPTION = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    public delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);

    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetDoubleClickTime();

    static readonly Guid SwapChainPanelNativeIid = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    /// <summary>Calls ISwapChainPanelNative::SetSwapChain on a WinUI 3 SwapChainPanel.</summary>
    public static unsafe void SetSwapChain(SwapChainPanel panel, nint swapChain)
    {
        nint unknown = ((IWinRTObject)panel).NativeObject.ThisPtr;
        Guid iid = SwapChainPanelNativeIid;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint native));
        try
        {
            var vtable = *(nint**)native;
            var setSwapChain = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
            Marshal.ThrowExceptionForHR(setSwapChain(native, swapChain));
        }
        finally
        {
            Marshal.Release(native);
        }
    }
}
