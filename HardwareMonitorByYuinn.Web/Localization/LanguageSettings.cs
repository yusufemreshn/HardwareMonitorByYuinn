using System.Globalization;
using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Localization;

/// <summary>
/// Seçili arayüz dilini (tr/en/de/ru/es/zh/fr/pt/ja) HKCU'da tutar; Desktop/DesktopShellSettings.cs'deki
/// CloseBehavior deseniyle aynı anahtar altında (ayrı bir değer adıyla). Hem web tarafı
/// (Program.cs başlangıç kültürü, SettingsController) hem masaüstü kabuğu (ShellForm/SplashForm/
/// RepairForm tepsi ve diyalog metinleri) bu sınıfı kullanır.
/// </summary>
public static class LanguageSettings
{
    private const string KeyPath = @"Software\HardwareMonitorByYuinn\DesktopShell";
    private const string ValueName = "Language";

    public const string Turkish = "tr";
    public const string English = "en";
    public const string German = "de";
    public const string Russian = "ru";
    public const string Spanish = "es";
    public const string Chinese = "zh";
    public const string French = "fr";
    public const string Portuguese = "pt";
    public const string Japanese = "ja";

    public static readonly IReadOnlyList<string> SupportedLanguages =
        [Turkish, English, German, Russian, Spanish, Chinese, French, Portuguese, Japanese];

    public static string GetLanguage()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        string? value = key?.GetValue(ValueName) as string;
        return value is not null && SupportedLanguages.Contains(value) ? value : Turkish;
    }

    public static void SetLanguage(string value)
    {
        string normalized = SupportedLanguages.Contains(value) ? value : Turkish;
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, normalized);
    }

    public static CultureInfo GetCultureInfo(string language) => CultureInfo.GetCultureInfo(language switch
    {
        English => "en-US",
        German => "de-DE",
        Russian => "ru-RU",
        Spanish => "es-ES",
        Chinese => "zh-CN",
        French => "fr-FR",
        Portuguese => "pt-BR",
        Japanese => "ja-JP",
        _ => "tr-TR",
    });
}
