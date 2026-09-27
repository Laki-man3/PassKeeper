using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PassKeeper.Views;

/// <summary>Content shown in the in-window modal layer of <see cref="MainWindow"/>.</summary>
public class DialogBase : UserControl
{
    public event Action<object?>? Closed;

    public double DialogWidth { get; set; } = 460;

    /// <summary>Element that receives focus when the dialog opens.</summary>
    public IInputElement? InitialFocus { get; set; }

    public void Close(object? result = null) => Closed?.Invoke(result);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
