using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct TAD_BANNED_APPS_INPUT
{
    public uint Count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 * 64)]
    public char[] ImageNames;
}

public class TadDriverClient
{
    private SafeFileHandle _handle;

    public bool Connect()
    {
        _handle = CreateFile(@"\\.\TAD_RV_Link", 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return !_handle.IsInvalid;
    }

    public void SetBannedApps(string[] appNames)
    {
        var input = new TAD_BANNED_APPS_INPUT { Count = (uint)appNames.Length, ImageNames = new char[32 * 64] };
        for (int i = 0; i < appNames.Length && i < 32; i++)
        {
            var chars = appNames[i].ToCharArray();
            int len = Math.Min(chars.Length, 63);
            Array.Copy(chars, 0, input.ImageNames, i * 64, len);
            input.ImageNames[i * 64 + len] = '\0';
        }
        int bytesReturned;
        DeviceIoControl(_handle, 0x8A002024, ref input, Marshal.SizeOf(input), IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, ref TAD_BANNED_APPS_INPUT lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);
}
