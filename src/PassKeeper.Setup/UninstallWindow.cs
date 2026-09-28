using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>Uninstaller UI: confirmation (with the optional removal of the user's data) → progress → result.</summary>
    internal sealed class UninstallWindow : WizardWindow
    {
        private readonly bool _machine;
        private readonly UninstallOptions _options;
        private CheckBox _removeData;

        public UninstallWindow(bool machine, UninstallOptions options) : base(560, 500)
        {
            _machine = machine;
            _options = options;
            ShowConfirm();
        }

        // ------------------------------------------------------------------ page 1: confirmation

        private void ShowConfirm()
        {
            Title = Texts.T("UnTitle");
            var panel = new StackPanel { Margin = new Thickness(36, 34, 36, 0) };
            panel.Children.Add(Header(Texts.T("UnTitle"),
                string.Format(Texts.T("UnVersion"), InstallerEngine.Version) + " · " + Texts.T(_machine ? "UnScopeAll" : "UnScopeUser"),
                ShowConfirm));

            var folderLabel = Text(Texts.T("Folder"), 12, B("TextSecondary"), FontWeights.SemiBold);
            folderLabel.Margin = new Thickness(0, 28, 0, 0);
            panel.Children.Add(folderLabel);
            var folder = PathText(UninstallEngine.InstallDirectory);
            folder.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(folder);
            var text = Text(Texts.T("UnText"), 13, B("TextSecondary"));
            text.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(text);

            // Data card: the vault is kept unless the user explicitly asks to remove it.
            var hasData = UninstallEngine.HasUserData;
            var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), BorderBrush = B("Border"), Background = B("SurfaceAlt"), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 22, 0, 0) };
            var cardStack = new StackPanel();
            _removeData = new CheckBox { IsChecked = _removeData?.IsChecked == true && hasData, IsEnabled = hasData };
            var checkText = new StackPanel();
            checkText.Children.Add(Text(Texts.T("UnRemoveData"), 14, null, FontWeights.SemiBold));
            checkText.Children.Add(Text(hasData ? Texts.T("UnRemoveDataDesc") : Texts.T("UnNoData"), 12, B("TextMuted")));
            _removeData.Content = checkText;
            _removeData.VerticalContentAlignment = VerticalAlignment.Top;
            cardStack.Children.Add(_removeData);
            if (hasData)
            {
                var dataPath = PathText(InstallLayout.UserDataDirectory());
                dataPath.Margin = new Thickness(28, 4, 0, 0);
                cardStack.Children.Add(dataPath);
            }
            card.Child = cardStack;
            panel.Children.Add(card);

            var note = new Grid { Margin = new Thickness(2, 14, 0, 0) };
            note.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            note.ColumnDefinitions.Add(new ColumnDefinition());
            var noteIcon = Glyph("\uE946", 14, B("TextMuted"));
            noteIcon.Margin = new Thickness(0, 1, 10, 0);
            note.Children.Add(noteIcon);
            var noteText = Text("", 12, B("TextMuted"));
            Grid.SetColumn(noteText, 1);
            note.Children.Add(noteText);
            if (hasData) panel.Children.Add(note);

            var uninstall = new Button { Content = Texts.T("UnButton"), Style = S("Button.Danger"), MinWidth = 130 };
            uninstall.Click += async (s, e) => await UninstallAsync();
            var cancel = new Button { Content = Texts.T("Cancel"), Style = S("Button.Ghost"), IsCancel = true, IsDefault = true };
            cancel.Click += (s, e) => Close();

            Action update = () =>
            {
                var remove = _removeData.IsChecked == true;
                card.BorderBrush = remove ? B("Danger") : B("Border");
                noteIcon.Text = remove ? "\uE7BA" : "\uE946";
                noteIcon.Foreground = remove ? B("Danger") : B("TextMuted");
                noteText.Text = Texts.T(remove ? "UnDataWarning" : "UnKeepDataNote");
                noteText.Foreground = remove ? B("Danger") : B("TextMuted");
            };
            _removeData.Checked += (s, e) => update();
            _removeData.Unchecked += (s, e) => update();
            update();

            var page = new Grid();
            page.Children.Add(panel);
            page.Children.Add(Footer(null, cancel, uninstall));
            ShowPage(page);
        }

        // ------------------------------------------------------------------ page 2: progress

        private async Task UninstallAsync()
        {
            _options.RemoveData = _removeData.IsChecked == true;
            var progress = ShowProgress(Texts.T("UnRemoving"), Texts.T("UnStepStopping"));
            string dataError = null;
            int code;
            try
            {
                code = await Task.Run(() => UninstallEngine.Run(_machine, _options, (p, text) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    progress.Item1.Value = p;
                    progress.Item2.Text = text;
                })), out dataError));
            }
            catch (Exception ex)
            {
                ShowResult(false, ex.Message);
                return;
            }
            if (code == InstallerEngine.ExitCancelled) ShowResult(false, Texts.T("UnCancelled"));
            else if (code != InstallerEngine.ExitOk) ShowResult(false, Texts.T("UnFailed") + " (" + code + ")");
            else ShowResult(true, dataError == null ? null : string.Format(Texts.T("UnDataBusy"), dataError));
        }

        // ------------------------------------------------------------------ page 3: result

        private void ShowResult(bool ok, string message)
        {
            StackPanel panel;
            if (!ok)
            {
                panel = ResultPanel(false, Texts.T("UnFailed"), message);
                ExitCode = InstallerEngine.ExitFailed;
            }
            else if (_options.RemoveData)
            {
                panel = ResultPanel(true, Texts.T("UnDone"), message ?? Texts.T("UnDoneRemoved"));
                ExitCode = InstallerEngine.ExitOk;
            }
            else
            {
                panel = ResultPanel(true, Texts.T("UnDone"), UninstallEngine.HasUserData ? Texts.T("UnDoneKept") : null);
                if (UninstallEngine.HasUserData)
                {
                    var path = PathText(InstallLayout.UserDataDirectory());
                    path.Margin = new Thickness(0, 6, 0, 0);
                    panel.Children.Add(path);
                }
                ExitCode = InstallerEngine.ExitOk;
            }
            var close = new Button { Content = Texts.T("Close"), Style = S("Button.Primary"), Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 130, IsDefault = true };
            close.Click += (s, e) => Close();
            panel.Children.Add(close);
            ShowPage(panel);
        }

        /// <summary>Developer aid ("/uninstall /render=dir"): saves the pages as PNG files without removing anything.</summary>
        internal async Task RenderPagesAsync(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            await Snap(System.IO.Path.Combine(dir, "uninstall-1-confirm-" + Texts.Language + ".png"));
            _removeData.IsChecked = true;
            await Snap(System.IO.Path.Combine(dir, "uninstall-2-remove-data-" + Texts.Language + ".png"));
            _options.RemoveData = false;
            ShowResult(true, null);
            await Snap(System.IO.Path.Combine(dir, "uninstall-3-done-" + Texts.Language + ".png"));
        }
    }
}
