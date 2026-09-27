using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PassKeeper.Controls;

/// <summary>Monospace password rendering: digits and symbols are colored for readability.</summary>
public static class PasswordText
{
    public static void Fill(TextBlock target, string password)
    {
        target.Inlines.Clear();
        foreach (var c in password)
        {
            var run = new Run(c.ToString());
            if (char.IsDigit(c)) run.SetResourceReference(TextElement.ForegroundProperty, "Brush.AccentText");
            else if (!char.IsLetter(c)) run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Warning");
            target.Inlines.Add(run);
        }
    }
}
