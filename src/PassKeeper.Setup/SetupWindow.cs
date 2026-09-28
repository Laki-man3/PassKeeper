using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>Installer UI: scope selection → progress → result.</summary>
    internal sealed class SetupWindow : WizardWindow
    {
        private readonly InstallOptions _options;
        private RadioButton _user, _all;
        private TextBlock _folderText;
        private CheckBox _desktop, _autostart, _launch;
        private string _customDir;

        public SetupWindow(InstallOptions options) : base(560, 600)
        {
            _options = options;
            Title = Texts.T("Title");
            ShowOptions();
        }

        // ------------------------------------------------------------------ page 1: options

        private void ShowOptions()
        {
            Title = Texts.T("Title");
            var existingAll = InstallerEngine.ExistingDirectory(true);
            var existingUser = InstallerEngine.ExistingDirectory(false);
            if (existingAll != null && existingUser == null && !_options.AllUsers) _options.AllUsers = true;

            var panel = new StackPanel { Margin = new Thickness(36, 34, 36, 0) };
            panel.Children.Add(Header(Texts.T("Title"), string.Format(Texts.T("Version"), InstallerEngine.Version), ShowOptions));

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
            _folderText = PathText("");
            _folderText.Margin = new Thickness(0, 4, 0, 0);
            folderStack.Children.Add(_folderText);
            folderRow.Children.Add(folderStack);
            var change = new Button { Content = Texts.T("Change"), Style = S("Button.Link"), VerticalAlignment = VerticalAlignment.Center };
            change.Click += (s, e) => ChooseFolder();
            Grid.SetColumn(change, 1);
            folderRow.Children.Add(change);
            panel.Children.Add(folderRow);

            _desktop = new CheckBox { Content = Texts.T("Desktop"), IsChecked = _desktop?.IsChecked ?? true, Margin = new Thickness(0, 20, 0, 0) };
            _autostart = new CheckBox { IsChecked = _autostart?.IsChecked ?? _options.Autostart, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(_desktop);
            panel.Children.Add(_autostart);

            var offline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lockIcon = Glyph("\uE72E", 12, B("TextMuted"));
            lockIcon.Margin = new Thickness(0, 1, 6, 0);
            offline.Children.Add(lockIcon);
            offline.Children.Add(Text(Texts.T("Offline"), 11.5, B("TextMuted")));
            var cancel = new Button { Content = Texts.T("Cancel"), Style = S("Button.Ghost") };
            cancel.Click += (s, e) => Close();
            var install = new Button { Content = Texts.T("Install"), Style = S("Button.Primary"), MinWidth = 130, IsDefault = true };
            install.Click += async (s, e) => await InstallAsync();

            var page = new Grid();
            page.Children.Add(panel);
            page.Children.Add(Footer(offline, cancel, install));
            ShowPage(page);
            UpdateScope();
        }

        private RadioButton ScopeCard(string icon, string title, string description, bool isChecked)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var iconBorder = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = B("Surface") };
            var glyph = Glyph(icon, 17, new SolidColorBrush(Color.FromRgb(0xA7, 0x96, 0xFF)));
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            iconBorder.Child = glyph;
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
            _options.AllUsers = allUsers;
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

            var progress = ShowProgress(Texts.T("Installing"), Texts.T("StepStopping"));
            var bar = progress.Item1;
            var status = progress.Item2;

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
            var panel = ResultPanel(ok, Texts.T(ok ? "Done" : "Failed"), ok ? Texts.T("DoneText") : error);
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
            ShowPage(panel);
            ExitCode = ok ? InstallerEngine.ExitOk : InstallerEngine.ExitFailed;
        }

        /// <summary>Developer aid ("/render=dir"): saves the setup pages as PNG files.</summary>
        internal async Task RenderPagesAsync(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            await Snap(System.IO.Path.Combine(dir, "setup-1-options-" + Texts.Language + ".png"));
            ShowResult(null);
            await Snap(System.IO.Path.Combine(dir, "setup-2-done-" + Texts.Language + ".png"));
        }
    }
}
