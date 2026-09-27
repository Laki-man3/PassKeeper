using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>Installer UI: scope selection → progress → result.</summary>
    internal sealed class SetupWindow : Window
    {
        private readonly InstallOptions _options;
        private readonly Grid _host = new Grid();
        private RadioButton _user, _all;
        private TextBlock _folderText;
        private CheckBox _desktop, _autostart, _launch;
        private string _customDir;

        public SetupWindow(InstallOptions options)
        {
            _options = options;
            Title = Texts.T("Title");
            Width = 560;
            Height = 600;
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
            root.Children.Add(_host);
            var close = new Button { Content = "\uE8BB", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Width = 46, Height = 32, Style = (Style)FindResource("Button.Ghost"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            close.Tag = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            close.Click += (s, e) => Close();
            root.Children.Add(close);
            Content = root;
            ShowOptions();
        }

        private Style S(string key) { return (Style)FindResource(key); }

        private TextBlock Text(string text, double size, Brush brush = null, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (brush != null) t.Foreground = brush;
            if (weight.HasValue) t.FontWeight = weight.Value;
            return t;
        }

        private Brush B(string key) { return (Brush)FindResource(key); }

        // ------------------------------------------------------------------ page 1: options

        private void ShowOptions()
        {
            var existingAll = InstallerEngine.ExistingDirectory(true);
            var existingUser = InstallerEngine.ExistingDirectory(false);
            if (existingAll != null && existingUser == null && !_options.AllUsers) _options.AllUsers = true;

            var panel = new StackPanel { Margin = new Thickness(36, 34, 36, 0) };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new Image { Source = (ImageSource)FindResource("Logo"), Width = 52, Height = 52 });
            var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text(Texts.T("Title"), 22, null, FontWeights.SemiBold));
            titles.Children.Add(Text(string.Format(Texts.T("Version"), InstallerEngine.Version), 12, B("TextMuted")));
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            var lang = LanguageSwitch();
            Grid.SetColumn(lang, 2);
            header.Children.Add(lang);
            panel.Children.Add(header);

            var scopeLabel = Text(Texts.T("Scope"), 12, B("TextSecondary"), FontWeights.SemiBold);
            scopeLabel.Margin = new Thickness(0, 28, 0, 10);
            panel.Children.Add(scopeLabel);

            _user = ScopeCard("\uE77B", Texts.T("ScopeUser"), Texts.T("ScopeUserDesc"), !_options.AllUsers);
            _all = ScopeCard("\uE716", Texts.T("ScopeAll"), Texts.T("ScopeAllDesc"), _options.AllUsers);
            _all.Margin = new Thickness(0, 10, 0, 0);
            _user.Checked += (s, e) => UpdateScope();
            _all.Checked += (s, e) => UpdateScope();
            panel.Children.Add(_user);
            panel.Children.Add(_all);

            var folderRow = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition());
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var folderStack = new StackPanel();
            folderStack.Children.Add(Text(Texts.T("Folder"), 12, B("TextSecondary"), FontWeights.SemiBold));
            _folderText = Text("", 12, B("TextMuted"));
            _folderText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            _folderText.Margin = new Thickness(0, 4, 0, 0);
            _folderText.TextTrimming = TextTrimming.CharacterEllipsis;
            _folderText.TextWrapping = TextWrapping.NoWrap;
            folderStack.Children.Add(_folderText);
            folderRow.Children.Add(folderStack);
            var change = new Button { Content = Texts.T("Change"), Style = S("Button.Link"), VerticalAlignment = VerticalAlignment.Center };
            change.Click += (s, e) => ChooseFolder();
            Grid.SetColumn(change, 1);
            folderRow.Children.Add(change);
            panel.Children.Add(folderRow);

            _desktop = new CheckBox { Content = Texts.T("Desktop"), IsChecked = true, Margin = new Thickness(0, 20, 0, 0) };
            _autostart = new CheckBox { IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(_desktop);
            panel.Children.Add(_autostart);

            var footer = new Border { BorderBrush = B("Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(36, 16, 36, 18), VerticalAlignment = VerticalAlignment.Bottom };
            var footerGrid = new Grid();
            var offline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            offline.Children.Add(new TextBlock { Text = "\uE72E", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = B("TextMuted"), Margin = new Thickness(0, 1, 6, 0) });
            offline.Children.Add(Text(Texts.T("Offline"), 11.5, B("TextMuted")));
            footerGrid.Children.Add(offline);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Texts.T("Cancel"), Style = S("Button.Ghost") };
            cancel.Click += (s, e) => Close();
            var install = new Button { Content = Texts.T("Install"), Style = S("Button.Primary"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 130, IsDefault = true };
            install.Click += async (s, e) => await InstallAsync();
            buttons.Children.Add(cancel);
            buttons.Children.Add(install);
            footerGrid.Children.Add(buttons);
            footer.Child = footerGrid;

            var page = new Grid();
            page.Children.Add(panel);
            page.Children.Add(footer);
            _host.Children.Clear();
            _host.Children.Add(page);
            UpdateScope();
        }

        private FrameworkElement LanguageSwitch()
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
                    Title = Texts.T("Title");
                    Dispatcher.BeginInvoke(new Action(ShowOptions));
                };
                stack.Children.Add(rb);
            }
            host.Child = stack;
            return host;
        }

        private RadioButton ScopeCard(string icon, string title, string description, bool isChecked)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var iconBorder = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = B("Surface") };
            iconBorder.Child = new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 17, Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0x96, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(iconBorder);
            var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Text(title, 14, null, FontWeights.SemiBold));
            texts.Children.Add(Text(description, 12, B("TextMuted")));
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            return new RadioButton { Style = S("ScopeCard"), Content = grid, GroupName = "scope", IsChecked = isChecked };
        }

        private void UpdateScope()
        {
            if (_all == null || _user == null) return;
            var allUsers = _all.IsChecked == true;
            var dir = _customDir ?? InstallerEngine.ExistingDirectory(allUsers) ?? InstallLayout.DefaultInstallDir(allUsers);
            _folderText.Text = dir;
            _folderText.ToolTip = dir;
            _autostart.Content = Texts.T(allUsers ? "AutostartAll" : "Autostart");
        }

        private void ChooseFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _folderText.Text, ShowNewFolderButton = true })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var path = dlg.SelectedPath;
                if (!path.TrimEnd('\\').EndsWith("\\" + InstallLayout.AppName, StringComparison.OrdinalIgnoreCase))
                    path = System.IO.Path.Combine(path, InstallLayout.AppName);
                _customDir = path;
                UpdateScope();
            }
        }

        // ------------------------------------------------------------------ page 2: progress

        private async Task InstallAsync()
        {
            _options.AllUsers = _all.IsChecked == true;
            _options.DesktopShortcut = _desktop.IsChecked == true;
            _options.Autostart = _autostart.IsChecked == true;
            _options.Directory = _customDir;
            _options.Language = Texts.Language;

            var bar = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 22, 0, 0) };
            var status = Text(Texts.T("StepStopping"), 12.5, B("TextSecondary"));
            status.Margin = new Thickness(0, 10, 0, 0);
            var panel = new StackPanel { Margin = new Thickness(36, 0, 36, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new Image { Source = (ImageSource)FindResource("Logo"), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left });
            var title = Text(Texts.T("Installing"), 22, null, FontWeights.SemiBold);
            title.Margin = new Thickness(0, 18, 0, 0);
            panel.Children.Add(title);
            panel.Children.Add(bar);
            panel.Children.Add(status);
            _host.Children.Clear();
            _host.Children.Add(panel);

            string error = null;
            if (_options.AllUsers && !InstallLayout.IsAdministrator())
            {
                var code = await Task.Run(() => InstallerEngine.RunElevated(_options));
                if (code == InstallerEngine.ExitCancelled) error = Texts.T("Cancelled");
                else if (code != InstallerEngine.ExitOk) error = Texts.T("Failed") + " (" + code + ")";
                bar.Value = 1;
            }
            else
            {
                try
                {
                    await Task.Run(() => InstallerEngine.Install(_options, (p, text) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        bar.Value = p;
                        status.Text = text;
                    }))));
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            ShowResult(error);
        }

        // ------------------------------------------------------------------ page 3: result

        private void ShowResult(string error)
        {
            var ok = error == null;
            var panel = new StackPanel { Margin = new Thickness(36, 0, 36, 0), VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(ok ? Color.FromArgb(0x26, 0x32, 0xC9, 0x8E) : Color.FromArgb(0x26, 0xF2, 0x60, 0x7A)) };
            badge.Child = new TextBlock { Text = ok ? "\uE73E" : "\uE711", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 28, Foreground = B(ok ? "Success" : "Danger"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(badge);
            var title = Text(Texts.T(ok ? "Done" : "Failed"), 22, null, FontWeights.SemiBold);
            title.Margin = new Thickness(0, 18, 0, 8);
            panel.Children.Add(title);
            panel.Children.Add(Text(ok ? Texts.T("DoneText") : error, 13, B("TextSecondary")));
            if (ok)
            {
                _launch = new CheckBox { Content = Texts.T("LaunchNow"), IsChecked = _options.Launch, Margin = new Thickness(0, 20, 0, 0) };
                panel.Children.Add(_launch);
            }
            var finish = new Button { Content = Texts.T(ok ? "Finish" : "Close"), Style = S("Button.Primary"), Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 130, IsDefault = true };
            finish.Click += (s, e) =>
            {
                if (ok && _launch != null && _launch.IsChecked == true) InstallerEngine.LaunchApp(_options.AllUsers, _options.Directory);
                ExitCode = ok ? InstallerEngine.ExitOk : InstallerEngine.ExitFailed;
                Close();
            };
            panel.Children.Add(finish);
            _host.Children.Clear();
            _host.Children.Add(panel);
            ExitCode = ok ? InstallerEngine.ExitOk : InstallerEngine.ExitFailed;
        }

        public int ExitCode { get; private set; } = InstallerEngine.ExitCancelled;

        /// <summary>Developer aid ("/render=dir"): saves the setup pages as PNG files.</summary>
        internal async Task RenderPagesAsync(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            await Snap(System.IO.Path.Combine(dir, "setup-1-options-" + Texts.Language + ".png"));
            ShowResult(null);
            await Snap(System.IO.Path.Combine(dir, "setup-2-done-" + Texts.Language + ".png"));
        }

        private async Task Snap(string path)
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
