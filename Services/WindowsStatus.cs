using System.Runtime.InteropServices;
namespace DBViewer.Services;
internal static class WindowsStatus
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="MessageBoxW")]
    static extern int MessageBox(IntPtr window,string text,string caption,uint type);
    public static void Show(string message,string title){if(OperatingSystem.IsWindows())MessageBox(IntPtr.Zero,message,title,0x40);}
}
