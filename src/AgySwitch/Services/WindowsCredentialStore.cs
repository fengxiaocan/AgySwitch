using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AgySwitch.Services;

public sealed record CredentialSnapshot(byte[] Blob, string? UserName, string? Comment, uint Persist);

public interface ICredentialStore
{
    CredentialSnapshot? Read(string target);
    void Write(string target, CredentialSnapshot credential);
    void Delete(string target);
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    public const string CurrentTarget = "gemini:antigravity";
    private const int NotFound = 1168;

    public static string ProfileTarget(AppPaths paths, string id) => $"AgySwitch:{id}";
    public static string RecoveryTarget(AppPaths paths) => "AgySwitch:recovery";
    public static string LegacyProfileTarget(AppPaths paths, string id) => $"AgySwitch:{Scope(paths)}:{id}";
    private static string Scope(AppPaths paths) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.GeminiDirectory.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));

    public CredentialSnapshot? Read(string target)
    {
        if (!CredRead(target, 1, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "无法读取 Windows 凭据。");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var blob = new byte[checked((int)credential.CredentialBlobSize)];
            if (blob.Length > 0) Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return new CredentialSnapshot(blob, credential.UserName, credential.Comment, credential.Persist);
        }
        finally { CredFree(pointer); }
    }

    public void Write(string target, CredentialSnapshot credential)
    {
        var blob = Marshal.AllocHGlobal(Math.Max(credential.Blob.Length, 1));
        try
        {
            if (credential.Blob.Length > 0) Marshal.Copy(credential.Blob, 0, blob, credential.Blob.Length);
            var native = new NativeCredential
            {
                Type = 1, TargetName = target, CredentialBlobSize = (uint)credential.Blob.Length,
                CredentialBlob = blob, Persist = credential.Persist, UserName = credential.UserName, Comment = credential.Comment
            };
            if (!CredWrite(ref native, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入 Windows 凭据。");
        }
        finally
        {
            for (var i = 0; i < credential.Blob.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string target)
    {
        if (!CredDelete(target, 1, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new Win32Exception(error, "无法删除 Windows 凭据。");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
