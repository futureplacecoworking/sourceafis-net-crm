using Microsoft.Win32;

namespace Futureplace.AgenteFS80H.Desktop;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FutureplaceAgenteFS80H";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, Command,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new IOException("Configuração de início indisponível.");
        if (enabled)
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, false);
    }
}
