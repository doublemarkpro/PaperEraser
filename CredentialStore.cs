using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PaperEraser;

internal static class CredentialStore
{
    internal const string DefaultTarget = "PaperEraser/QuarkApi";
    private const int Generic = 1, LocalMachine = 2, NotFound = 1168;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    internal static void Save(string id, string secret, string target = DefaultTarget)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560) throw new ArgumentException("Client Secret 过长，无法保存到 Windows 凭据管理器。");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new Credential { Type = Generic, TargetName = target, UserName = id, CredentialBlob = buffer, CredentialBlobSize = bytes.Length, Persist = LocalMachine };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Array.Clear(bytes); for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(buffer, i, 0); Marshal.FreeHGlobal(buffer); }
    }
    internal static (string Id, string Secret)? Load(string target = DefaultTarget)
    {
        if (!CredRead(target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error(); if (error == NotFound) return null; throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return (credential.UserName ?? "", Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2) ?? "");
        }
        finally { CredFree(pointer); }
    }
    internal static void Delete(string target = DefaultTarget)
    {
        if (!CredDelete(target, Generic, 0)) { var error = Marshal.GetLastWin32Error(); if (error != NotFound) throw new Win32Exception(error); }
    }
}
