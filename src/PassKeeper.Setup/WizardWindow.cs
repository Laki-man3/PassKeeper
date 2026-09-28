using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace PassKeeper.Setup
{
    /// <summary>Borderless dark window shared by the installer and the uninstaller.</summary>
    internal abstract class WizardWindow : Window
    {
        protected readonly Grid Host = new Grid();

        protected WizardWindow(double width, double height)
        {
            Width = width;
            Height = height;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            Background = (Brush)FindResource("Bg");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            SetValue(TextElement.ForegroundProperty, FindResource("Text"));
            UseLayoutRounding = true;
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/PassKeeper-Setup;component/Assets/PassKeeper.ico"));
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false });

            var root = new Grid();
            root.Children.Add(Host);
            var close = new Button { Content = "\uE8BB", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Width = 46, Height = 32, Style = S("Button.Ghost"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            close.Tag = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            close.Click += (s, e) => Close();
            root.Children.Add(close);
            Content = root;
        }

        public int ExitCode { get; protected set; } = InstallerEngine.ExitCancelled;

        protected Style S(string key) { return (Style)FindResource(key); }

        protected Brush B(string key) { return (Brush)FindResource(key); }

        protected TextBlock Text(string text, double size, Brush brush = null, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (brush != null) t.Foreground = brush;
            if (weight.HasValue) t.FontWeight = weight.Value;
            return t;
        }

        protected TextBlock Glyph(string glyph, double size, Brush brush)
        {
            return new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = size, Foreground = brush };
        }

        protected TextBlock PathText(string path)
        {
            var t = Text(path, 12, B("TextMuted"));
            t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.TextWrapping = TextWrapping.NoWrap;
            t.ToolTip = path;
            return t;
        }

        /// <summary>Logo, title, subtitle and the RU/EN switch.</summary>
        protected Grid Header(string title, string subtitle, Action languageChanged)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new Image { Source = (ImageSource)FindResource("Logo"), Width = 52, Height = 52 });
            var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text(title, 22, null, FontWeights.SemiBold));
            titles.Children.Add(Text(subtitle, 12, B("TextMuted")));
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            var lang = LanguageSwitch(languageChanged);
            Grid.SetColumn(lang, 2);
            header.Children.Add(lang);
            return header;
        }

        private FrameworkElement LanguageSwitch(Action changed)
        {
            var host = new Border { Background = B("SurfaceAlt"), BorderBrush = B("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 30, 0) };
            WindowChrome.SetIsHitTestVisibleInChrome(host, true);
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var code in new[] { "ru", "en" })
            {
                var rb = new RadioButton { Content = code.ToUpperInvariant(), Style = S("Segment"), GroupName = "lang", IsChecked = Texts.Language == code };
                var c = code;
                rb.Checked += (s, e) =>
                {
                    if (Texts.Language == c) return;
                    Texts.Language = c;
                    Dispatcher.BeginInvoke(changed);
                };
                stack.Children.Add(rb);
            }
            host.Child = stack;
            return host;
        }

        /// <summary>Bottom bar: note on the left, buttons on the right.</summary>
        protected Border Footer(UIElement left, params Button[] buttons)
        {
            var footer = new Border { BorderBrush = B("Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(36, 16, 36, 18), VerticalAlignment = VerticalAlignment.Bottom };
            var grid = new Grid();
            if (left != null) grid.Children.Add(left);
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (var i = 0; i < buttons.Length; i++)
            {
                if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
                stack.Children.Add(buttons[i]);
            }
            grid.Children.Add(stack);
            footer.Child = grid;
            return footer;
        }

        protected void ShowPage(UIElement page)
        {
            Host.Children.Clear();
            Host.Children.Add(page);
        }

        /// <summary>Progress page; returns the bar and the status line to update.</summary>
        protected Tuple<ProgressBar, TextBlock> ShowProgress(string title, string status)
        {
            var bar = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 22, 0, 0) };
            var statusText = Text(status, 12.5, B("TextSecondary"));
            statusText.Margin = new Thickness(0, 10, 0, 0);
            var panel = new StackPanel { Margin = new Thickness(36, 0, 36, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new Image { Source = (ImageSource)FindResource("Logo"), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left });
            var head = Text(title, 22, null, FontWeights.SemiBold);
            head.Margin = new Thickness(0, 18, 0, 0);
            panel.Children.Add(head);
            panel.Children.Add(bar);
            panel.Children.Add(statusText);
            ShowPage(panel);
            return Tuple.Create(bar, statusText);
        }

        /// <summary>Result page with a success/failure badge. Extra content goes between the text and the button.</summary>
        protected StackPanel ResultPanel(bool ok, string title, string text)
        {
            var panel = new StackPanel { Margin = new Thickness(36, 0, 36, 0), VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(ok ? Color.FromArgb(0x26, 0x32, 0xC9, 0x8E) : Color.FromArgb(0x26, 0xF2, 0x60, 0x7A)) };
            var mark = Glyph(ok ? "\uE73E" : "\uE711", 28, B(ok ? "Success" : "Danger"));
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = mark;
            panel.Children.Add(badge);
            var head = Text(title, 22, null, FontWeights.SemiBold);
            head.Margin = new Thickness(0, 18, 0, 8);
            panel.Children.Add(head);
            if (!string.IsNullOrEmpty(text)) panel.Children.Add(Text(text, 13, B("TextSecondary")));
            return panel;
        }

        /// <summary>Developer aid: saves the current page as PNG.</summary>
        internal async Task Snap(string path)
        {
            await Task.Delay(400);
            var root = (FrameworkElement)Content;
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * 1.25), (int)(root.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bmp.Render(bg);
            bmp.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 44) DragMove();
        }
    }
}
