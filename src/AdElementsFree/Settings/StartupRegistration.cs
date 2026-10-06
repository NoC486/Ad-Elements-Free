using Microsoft.Win32;

namespace AdElementsFree.Settings;

public sealed class StartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "AdElementsFree";
    private readonly string keyPath;
    private readonly string command;

    // Alternative key is used by isolated tests; production always uses HKCU Run.
    public StartupRegistration(string executable, string keyPath = RunKey)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"'))
            throw new ArgumentException("An absolute executable path is required.", nameof(executable));
        command = $"\"{executable}\" --startup";
        if (command.Length > 260) throw new ArgumentException("Startup command is too long.", nameof(executable));
        this.keyPath = keyPath;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return string.Equals(key?.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
        if (enabled) key.SetValue(ValueName, command, RegistryValueKind.String);
        // Do not remove the registration of another installed/portable copy.
        else if (string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            key.DeleteValue(ValueName, false);
    }
}
