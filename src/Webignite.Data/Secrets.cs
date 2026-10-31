using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Webignite.Data;

/// <summary>Passwords and other secrets, kept outside the database.</summary>
public interface ISecretStore
{
    string? Read(string name);
    void Write(string name, string secret);
    void Delete(string name);
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> secrets = [];
    public string? Read(string name) => secrets.GetValueOrDefault(name);
    public void Write(string name, string secret) => secrets[name] = secret;
    public void Delete(string name) => secrets.Remove(name);
}

/// <summary>
/// Windows Credential Manager, under the signed-in Windows user. Each secret shows there as
/// "Webignite Desktop/{name}", e.g. "Webignite Desktop/Mail/eddie@webignite.co.za".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStore : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    private static string Target(string name) => "Webignite Desktop/" + name;

    public string? Read(string name)
    {
        if (!CredRead(Target(name), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string name, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(name),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = name,
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string name)
    {
        if (!CredDelete(Target(name), CredTypeGeneric, 0) && Marshal.GetLastPInvokeError() is var error and not ErrorNotFound)
            throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
