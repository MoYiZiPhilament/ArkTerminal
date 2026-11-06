using System.Globalization;
using ArkTerminal.Core.Configuration;

namespace ArkTerminal.Core.Localization;

public interface ILocalizationService
{
    CultureInfo Culture { get; }
    TimeZoneInfo TimeZone { get; }
    event Action? Changed;
    string Get(string key);
    string RefreshText(string value);
    string Format(string key, params object?[] arguments);
    DateTimeOffset ToConfiguredTime(DateTimeOffset value);
    void Configure(RegionalSettings settings);
}

public static class LocalizationDefaults
{
    public const string English = "en-US";
    public const string Chinese = "zh-CN";

    private static readonly string InitialSystemLanguage =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? Chinese
            : English;

    public static string SystemLanguage() => InitialSystemLanguage;

    public static bool IsSupportedLanguage(string? language) =>
        language is English or Chinese;

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }
}
