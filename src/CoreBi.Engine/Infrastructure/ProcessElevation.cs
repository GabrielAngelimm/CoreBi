using System.Security.Principal;

namespace CoreBi.Engine.Infrastructure;

public static class ProcessElevation
{
    public static bool IsElevated { get; } = ReadIsElevated();

    private static bool ReadIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
