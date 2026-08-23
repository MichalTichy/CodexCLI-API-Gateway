using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexGateway.Infrastructure.Codex;

internal static class UnixIdentity
{
    internal static bool IsRoot() =>
        OperatingSystem.IsLinux() ? GetEffectiveUserIdLinux() == 0 :
        OperatingSystem.IsMacOS() && GetEffectiveUserIdMacOs() == 0;

    internal static string? GetContainerUser()
    {
        // Docker Desktop bind mounts do not grant the image's fixed UID write access
        // to the Windows-hosted Codex authentication directory.
        if (OperatingSystem.IsWindows())
        {
            return "0:0";
        }

        if (OperatingSystem.IsLinux())
        {
            var user = GetEffectiveUserIdLinux();
            return user == 0 ? null : $"{user.ToString(CultureInfo.InvariantCulture)}:{GetEffectiveGroupIdLinux().ToString(CultureInfo.InvariantCulture)}";
        }

        if (OperatingSystem.IsMacOS())
        {
            var user = GetEffectiveUserIdMacOs();
            return user == 0 ? null : $"{user.ToString(CultureInfo.InvariantCulture)}:{GetEffectiveGroupIdMacOs().ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserIdLinux();

    [DllImport("libc", EntryPoint = "getegid")]
    private static extern uint GetEffectiveGroupIdLinux();

    [DllImport("libSystem.B.dylib", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserIdMacOs();

    [DllImport("libSystem.B.dylib", EntryPoint = "getegid")]
    private static extern uint GetEffectiveGroupIdMacOs();
}
