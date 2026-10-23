using System.Security.AccessControl;
using System.Security.Principal;

namespace CoreBi.Engine.Infrastructure;

internal static class ProtectedWorkspace
{
    /// <summary>Privileged artifacts must not be writable by a medium-integrity process of the same user.</summary>
    public static string GetFolder(string area)
    {
        if (area is not ("Downloads" or "Startup")) throw new ArgumentException("Área inválida.", nameof(area));
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CoreBi");
        Ensure(root);
        string user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Usuário indisponível.");
        string folder = Path.Combine(root, user, area);
        Ensure(Path.Combine(root, user));
        Ensure(folder);
        return folder;
    }

    private static void Ensure(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists) { Verify(directory); return; }
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.Create(security);
        directory.Refresh();
        Verify(directory); // Also reject a directory created by someone else during CreateDirectory.
    }

    private static void Verify(DirectoryInfo directory)
    {
        if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A pasta protegida não pode ser um link ou ponto de redirecionamento.");
        var security = directory.GetAccessControl();
        static bool Trusted(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || sid.IsWellKnown(WellKnownSidType.LocalSystemSid);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (owner == null || !Trusted(owner) || !security.AreAccessRulesProtected || rules.Length != 2
            || rules.Any(rule => rule.IdentityReference is not SecurityIdentifier sid || !Trusted(sid)
                || rule.AccessControlType != AccessControlType.Allow || rule.FileSystemRights != FileSystemRights.FullControl))
            throw new IOException("A pasta administrativa existente não possui as permissões restritas esperadas.");
    }
}
