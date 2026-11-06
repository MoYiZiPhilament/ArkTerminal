using System.Globalization;
using System.Resources;
using ArkTerminal.Core.Configuration;

namespace ArkTerminal.Core.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resources = new("ArkTerminal.Core.Localization.Strings", typeof(LocalizationService).Assembly);

    public LocalizationService() => Configure(new RegionalSettings());

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(LocalizationDefaults.English);
    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Local;
    public event Action? Changed;

    public string Get(string key) => _resources.GetString(key, Culture) ?? key;

    public string RefreshText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        foreach (var language in new[] { LocalizationDefaults.English, LocalizationDefaults.Chinese })
        {
            if (language == Culture.Name) continue;
            var culture = CultureInfo.GetCultureInfo(language);
            var set = _resources.GetResourceSet(culture, true, true);
            if (set is null) continue;
            foreach (System.Collections.DictionaryEntry entry in set)
            {
                if (entry.Value is string text && text == value && entry.Key is string key)
                    return Get(key);
            }
        }
        return value;
    }

    public string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);

    public DateTimeOffset ToConfiguredTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone);

    public void Configure(RegionalSettings settings)
    {
        var language = LocalizationDefaults.IsSupportedLanguage(settings.Language)
            ? settings.Language
            : LocalizationDefaults.English;
        var culture = CultureInfo.GetCultureInfo(language);
        var timeZone = LocalizationDefaults.ResolveTimeZone(settings.TimeZoneId);
        if (Culture.Name == culture.Name && TimeZone.Id == timeZone.Id) return;
        Culture = culture;
        TimeZone = timeZone;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Changed?.Invoke();
    }
}

public static class Localize
{
    private static readonly LocalizationService Service = new();

    public static ILocalizationService Current => Service;
    public static event Action? Changed { add => Service.Changed += value; remove => Service.Changed -= value; }
    public static string T(string key) => Service.Get(key);
    public static string RefreshText(string value) => Service.RefreshText(value);
    public static string F(string key, params object?[] arguments) => Service.Format(key, arguments);
    public static DateTimeOffset ToConfiguredTime(DateTimeOffset value) => Service.ToConfiguredTime(value);
    public static void Configure(RegionalSettings? settings) => Service.Configure(settings ?? new RegionalSettings());
}
