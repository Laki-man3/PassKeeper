using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PassKeeper.Localization;

/// <summary>Runtime-switchable RU/EN string table. XAML binds through <see cref="TExtension"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _language = DefaultLanguage();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public static string DefaultLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? "ru" : "en";

    public string Language
    {
        get => _language;
        set
        {
            var lang = value == "en" ? "en" : "ru";
            if (lang == _language) return;
            _language = lang;
            var culture = CultureInfo.GetCultureInfo(lang == "ru" ? "ru-RU" : "en-US");
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsRussian => _language == "ru";

    public string this[string key] => T(key);

    public static string T(string key)
    {
        if (Strings.Table.TryGetValue(key, out var pair)) return I._language == "ru" ? pair.Ru : pair.En;
        return "[" + key + "]";
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>"1 запись", "2 записи", "5 записей" / "1 entry", "2 entries".</summary>
    public static string Plural(int n, string key)
    {
        var forms = T(key).Split('|');
        if (I._language == "ru" && forms.Length >= 3)
        {
            var mod10 = n % 10;
            var mod100 = n % 100;
            var form = mod10 == 1 && mod100 != 11 ? forms[0]
                : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? forms[1]
                : forms[2];
            return n + " " + form;
        }
        return n + " " + (n == 1 ? forms[0] : forms[Math.Min(1, forms.Length - 1)]);
    }
}

/// <summary>Usage: <c>Text="{l:T Settings.Title}"</c>. Updates live when the language changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
