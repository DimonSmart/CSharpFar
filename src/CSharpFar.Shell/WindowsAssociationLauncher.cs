using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CSharpFar.Shell;

internal sealed class WindowsAssociationLauncher : IWindowsAssociationLauncher
{
    private const uint SeeMaskFlagNoUi = 0x00000400;
    private const uint SeeMaskNoConsole = 0x00008000;

    public void OpenDetached(WindowsAssociationLaunchRequest request)
    {
        var executeInfo = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskFlagNoUi | (request.SuppressConsole ? SeeMaskNoConsole : 0),
            lpVerb = request.Verb,
            lpFile = request.FullPath,
            lpDirectory = request.WorkingDirectory,
            nShow = 1,
        };

        if (!ShellExecuteEx(ref executeInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open file: {request.FullPath}");
    }

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo executeInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public nint hKeyClass;
        public uint dwHotKey;
        public nint hIcon;
        public nint hProcess;
    }
}
