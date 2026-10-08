using System.Globalization;
using System.Resources;

namespace CodexGateway.App.Components.Pages.Admin.Shared;

internal static class AdminUx
{
    private static readonly ResourceManager Resources = new(
        "CodexGateway.App.Components.Pages.Admin.Shared.AdminUx", typeof(AdminUx).Assembly);

    public static string Text(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Text(key), args);
}
