using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Diktat;

internal sealed class AppSettings
{
    // Tastenkürzel (Standard: Strg + Alt + D)
    public uint HotkeyModifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;
    public int HotkeyKey { get; set; } = 0x44; // 'D'

    // Spracherkennung
    public string WhisperModel { get; set; } = "small";
    public string Language { get; set; } = "de";
    public int MicrophoneDevice { get; set; } = -1; // -1 = Windows-Standardmikrofon
    public string Vocabulary { get; set; } = "";

    // KI-Nachbearbeitung
    public bool AiEnabled { get; set; } = true;
    public string AiModel { get; set; } = "claude-opus-5-5";
    public string AiExtraInstructions { get; set; } = "";
    public string? EncryptedApiKey { get; set; }

    // Sonstiges
    public bool SaveHistory { get; set; } = true;

    [JsonIgnore]
    public string ApiKey
    {
        get
        {
            if (!string.IsNullOrEmpty(EncryptedApiKey))
            {
                try
                {
                    var bytes = ProtectedData.Unprotect(Convert.FromBase64String(EncryptedApiKey), Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (Exception ex)
                {
                    Log.Error("API-Schlüssel konnte nicht entschlüsselt werden", ex);
                }
            }
            return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        }
        set
        {
            EncryptedApiKey = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser));
        }
    }

    [JsonIgnore]
    public string HotkeyText => Hotkey.Describe(HotkeyModifiers, HotkeyKey);

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Diktat.v1");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen konnten nicht geladen werden", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        var tmp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    // ---- Autostart über HKCU\...\Run ----

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Diktat";

    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
