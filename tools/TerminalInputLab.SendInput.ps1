param(
    [string] $WindowTitle = "CSharpFar Terminal Input Lab"
)

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class TerminalInputLabNative
{
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    public static Dictionary<IntPtr,string> Windows() { var r=new Dictionary<IntPtr,string>(); EnumWindows((h,p)=>{var s=new StringBuilder(512); GetWindowText(h,s,s.Capacity); if(s.Length>0)r[h]=s.ToString(); return true;},IntPtr.Zero); return r; }
    static INPUT Key(ushort vk, bool up=false) => new INPUT { type=1, U=new InputUnion { ki=new KEYBDINPUT { wVk=vk, dwFlags=up?2u:0u } } };
    public static void Chord(params ushort[] keys) { var a=new List<INPUT>(); foreach(var k in keys)a.Add(Key(k)); for(int i=keys.Length-1;i>=0;i--)a.Add(Key(keys[i],true)); SendInput((uint)a.Count,a.ToArray(),Marshal.SizeOf<INPUT>()); }
    public static void Mouse(uint flags, uint data=0) { var a=new[]{new INPUT { type=0, U=new InputUnion { mi=new MOUSEINPUT { dx=20,dy=10,mouseData=data,dwFlags=flags } } }}; SendInput(1,a,Marshal.SizeOf<INPUT>()); }
}
'@

Add-Type -TypeDefinition $source
$windows = [TerminalInputLabNative]::Windows()
$windows.GetEnumerator() | Sort-Object Value | ForEach-Object { "0x{0:X}: {1}" -f $_.Key.ToInt64(), $_.Value }
$target = $windows.GetEnumerator() | Where-Object Value -Like "*$WindowTitle*" | Select-Object -First 1
if (-not $target) { throw "No window title contains '$WindowTitle'." }
[void][TerminalInputLabNative]::SetForegroundWindow($target.Key)
Start-Sleep -Milliseconds 500

$VK_SHIFT=0x10; $VK_CONTROL=0x11; $VK_MENU=0x12; $VK_A=0x41; $VK_UP=0x26; $VK_RIGHT=0x27; $VK_F5=0x74
[TerminalInputLabNative]::Chord($VK_A); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Chord($VK_UP); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Chord($VK_F5); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Chord($VK_SHIFT,$VK_F5); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Chord($VK_MENU,$VK_A); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Chord($VK_CONTROL,$VK_RIGHT); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Mouse(0x0001); Start-Sleep -Milliseconds 150
[TerminalInputLabNative]::Mouse(0x0002); [TerminalInputLabNative]::Mouse(0x0004); Start-Sleep -Milliseconds 150
$wheelDown = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]-120), 0)
[TerminalInputLabNative]::Mouse(0x0800,120); [TerminalInputLabNative]::Mouse(0x0800,$wheelDown)

Write-Host "Synthetic input sent. Manual physical keyboard/mouse test is still required."
