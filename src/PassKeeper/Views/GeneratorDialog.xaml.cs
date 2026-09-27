using System.Windows;
using PassKeeper.Controls;
using PassKeeper.Core.Security;
using PassKeeper.Localization;

namespace PassKeeper.Views;

public partial class GeneratorDialog : DialogBase
{
    public static GeneratorOptions LastOptions { get; } = new();
    private readonly bool _pickMode;
    private bool _loading = true;
    private string _password = "";

    public GeneratorDialog(bool pickMode)
    {
        InitializeComponent();
        _pickMode = pickMode;
        DialogWidth = 480;
        var o = LastOptions;
        LengthSlider.Value = o.Length;
        Upper.IsChecked = o.Upper;
        Lower.IsChecked = o.Lower;
        Digits.IsChecked = o.Digits;
        Symbols.IsChecked = o.Symbols;
        Ambiguous.IsChecked = o.ExcludeAmbiguous;
        UseButton.Visibility = pickMode ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = pickMode ? Visibility.Collapsed : Visibility.Visible;
        _loading = false;
        Regenerate();
        InitialFocus = pickMode ? UseButton : CopyButton;
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var o = LastOptions;
        o.Length = (int)LengthSlider.Value;
        o.Upper = Upper.IsChecked == true;
        o.Lower = Lower.IsChecked == true;
        o.Digits = Digits.IsChecked == true;
        o.Symbols = Symbols.IsChecked == true;
        o.ExcludeAmbiguous = Ambiguous.IsChecked == true;
        if (!o.Upper && !o.Lower && !o.Digits && !o.Symbols)
        {
            o.Lower = true;
            Lower.IsChecked = true;
        }
        Regenerate();
    }

    private void Regenerate()
    {
        _password = PasswordGenerator.Generate(LastOptions);
        PasswordText.Fill(Output, _password);
        Meter.Password = _password;
        LengthText.Text = ((int)LengthSlider.Value).ToString();
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e) => Regenerate();

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        App.Instance.CopySecret(_password, Loc.T("Toast.PasswordCopied"), null);

    private void CopyClose_Click(object sender, RoutedEventArgs e)
    {
        Copy_Click(sender, e);
        Close(null);
    }

    private void Use_Click(object sender, RoutedEventArgs e) => Close(_password);

    private void Close_Click(object sender, RoutedEventArgs e) => Close(null);
}
