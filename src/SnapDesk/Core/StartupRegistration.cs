using Microsoft.Win32;

namespace SnapDesk.Core;

/// <summary>
/// Windows oturum açılışında SnapDesk'in sistem tepsisinde hazır başlaması için kayıt yönetimi.
/// </summary>
public static class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "SnapDesk";

    public static string BuildStartupCommand(string executablePath)
    {
        var clean = executablePath.Trim().Trim('"');
        return $"\"{clean}\" --tray";
    }

    public static bool ShouldManageExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(executablePath.Trim().Trim('"'));
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !fileName.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static void Sync(bool enabled, string? executablePath = null)
    {
        executablePath ??= Environment.ProcessPath;
        if (enabled && !ShouldManageExecutable(executablePath))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, BuildStartupCommand(executablePath!), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Kayıt defteri izni kısıtlıysa uygulamayı durdurma.
        }
    }

    public static string? ReadRegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
