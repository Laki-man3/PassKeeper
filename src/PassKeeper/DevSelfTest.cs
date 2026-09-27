using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using PassKeeper.Services;

namespace PassKeeper;

/// <summary>
/// Developer aid: "PassKeeper.exe --selftest report.txt [--browser path\to\browser.exe]" exercises the autofill
/// pipeline (UI Automation field discovery, URL detection, focus events, SendInput typing) against a local test
/// window and, optionally, a browser started with a throw-away profile on a local HTML login page.
/// </summary>
internal static class DevSelfTest
{
    private const string Login = "user@пример.рф";
    private const string Password = "P@ss-Ёж 123!";

    public static async void Run(string reportPath, string? browser)
    {
        var log = new StringBuilder();
        void L(string s) => log.AppendLine(s);
        try
        {
            await TestOwnWindow(L);
            if (!string.IsNullOrEmpty(browser)) await TestBrowser(browser, L);
        }
        catch (Exception ex)
        {
            L("EXCEPTION: " + ex);
        }
        File.WriteAllText(reportPath, log.ToString(), Encoding.UTF8);
        App.Instance.Shutdown();
    }

    private static async Task TestOwnWindow(Action<string> L)
    {
        L("== WPF test window");
        var login = new TextBox { Margin = new Thickness(16) };
        System.Windows.Automation.AutomationProperties.SetName(login, "Login");
        var password = new PasswordBox { Margin = new Thickness(16) };
        System.Windows.Automation.AutomationProperties.SetName(password, "Password");
        var panel = new StackPanel();
        panel.Children.Add(login);
        panel.Children.Add(password);
        var window = new Window { Title = "PassKeeper self-test login", Width = 420, Height = 220, Content = panel, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Show();
        window.Activate();
        login.Focus();
        await Task.Delay(600);
        var hwnd = new WindowInteropHelper(window).Handle;
        Native.ForceForeground(hwnd);

        await Task.Run(() =>
        {
            var target = TargetDetector.Capture(hwnd, detectUrl: false);
            L($"target: title='{target.Title}' process='{target.ProcessName}' browser={target.IsBrowser}");
            var focused = AutomationElement.FocusedElement;
            L($"focused: type={focused.Current.ControlType.ProgrammaticName} password={focused.Current.IsPassword} name='{focused.Current.Name}'");
            var pwd = FieldFinder.FindPasswordAfter(focused);
            L($"password field after login: {(pwd != null ? "found" : "NOT FOUND")}");
            var sender = new KeyboardSender { KeyDelayMs = 4, TargetWindow = hwnd };
            KeyboardSender.WaitForModifiersReleased();
            sender.ClearField();
            sender.TypeText(Login);
            FieldFinder.WaitForValue(focused, Login);
            if (pwd != null)
            {
                FieldFinder.Focus(pwd);
                sender.ClearField();
                sender.TypeText(Password);
                var back = FieldFinder.FindUsernameBefore(pwd);
                L($"login field before password: {(back != null ? "found" : "NOT FOUND")}");
            }
        });
        await Task.Delay(300);
        L($"RESULT unicode typing: login {(login.Text == Login ? "OK" : "FAIL '" + login.Text + "'")}, password {(password.Password == Password ? "OK" : "FAIL '" + password.Password + "'")}");

        // Compatibility (virtual-key) mode with ASCII text.
        login.Focus();
        await Task.Delay(200);
        await Task.Run(() =>
        {
            var sender = new KeyboardSender { KeyDelayMs = 4, TargetWindow = hwnd, UseVirtualKeys = true };
            sender.ClearField();
            sender.TypeText("Admin_2026");
        });
        await Task.Delay(300);
        L($"RESULT virtual-key typing: {(login.Text == "Admin_2026" ? "OK" : "FAIL '" + login.Text + "'")}");
        window.Close();
    }

    private static async Task TestBrowser(string browser, Action<string> L)
    {
        L($"== Browser test: {browser}");
        var testStarted = DateTime.Now.AddSeconds(-1);
        var gecko = Path.GetFileNameWithoutExtension(browser).Equals("firefox", StringComparison.OrdinalIgnoreCase);
        var tmp = Path.Combine(Path.GetTempPath(), "pk-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var html = Path.Combine(tmp, "login.html");
        File.WriteAllText(html, """
            <!doctype html><html><head><meta charset="utf-8"><title>Login test</title></head><body>
            <form style="margin:40px;font:16px sans-serif">
              <p><input id="u" name="username" placeholder="Email or login" aria-label="Email or login" autocomplete="username"></p>
              <p><input id="p" type="password" name="password" aria-label="Password" autocomplete="current-password"></p>
              <p><button type="button">Sign in</button></p>
            </form>
            <script>
              function t(){ document.title = 'U=' + document.getElementById('u').value + ';P=' + document.getElementById('p').value; }
              document.getElementById('u').addEventListener('input', t);
              document.getElementById('p').addEventListener('input', t);
            </script></body></html>
            """, Encoding.UTF8);
        var profile = Path.Combine(tmp, "profile");
        var url = new Uri(html).AbsoluteUri;
        var args = gecko
            ? $"-no-remote -new-instance -profile \"{profile}\" \"{url}\""
            : $"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --disable-sync --disable-extensions --new-window \"{url}\"";
        Directory.CreateDirectory(profile);
        if (gecko)
        {
            // Throw-away profile: skip onboarding / terms-of-use modals that would steal focus.
            File.WriteAllText(Path.Combine(profile, "user.js"), string.Join("\n",
                "user_pref(\"browser.aboutwelcome.enabled\", false);",
                "user_pref(\"browser.preonboarding.enabled\", false);",
                "user_pref(\"termsofuse.bypassNotification\", true);",
                "user_pref(\"datareporting.policy.dataSubmissionPolicyBypassNotification\", true);",
                "user_pref(\"toolkit.telemetry.reportingpolicy.firstRun\", false);",
                "user_pref(\"browser.startup.homepage_override.mstone\", \"ignore\");",
                "user_pref(\"browser.shell.checkDefaultBrowser\", false);",
                "user_pref(\"trailhead.firstrun.didSeeAboutWelcome\", true);"));
        }
        var process = Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false });

        var watcher = new FocusWatcher();
        var events = new List<string>();
        watcher.LoginFieldFocused += f => { lock (events) events.Add(f.IsPassword ? "password" : "login"); };
        watcher.Start();

        try
        {
            var hwnd = IntPtr.Zero;
            for (var i = 0; i < 60 && hwnd == IntPtr.Zero; i++)
            {
                await Task.Delay(500);
                hwnd = FindWindow("Login test");
            }
            L($"browser window: {(hwnd != IntPtr.Zero ? "found" : "NOT FOUND")}");
            if (hwnd == IntPtr.Zero) return;
            Native.ForceForeground(hwnd);
            await Task.Delay(2500);

            await Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(hwnd);
                AutomationElement? user = null;
                for (var attempt = 0; attempt < 10 && user == null; attempt++)
                {
                    var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
                        .Cast<AutomationElement>().ToList();
                    if (attempt == 0) L("edits: " + string.Join(" | ", edits.Select(e => $"'{e.Current.Name}' pwd={e.Current.IsPassword}")));
                    user = edits.FirstOrDefault(e => e.Current.Name.Contains("Email or login", StringComparison.OrdinalIgnoreCase));
                    if (user == null) Thread.Sleep(700);
                }
                L($"login field via UIA: {(user != null ? "found" : "NOT FOUND")}");
                if (user == null) return;
                FieldFinder.Focus(user);
                Thread.Sleep(800);
                var target = TargetDetector.Capture(hwnd, detectUrl: true, focused: user);
                L($"target: process='{target.ProcessName}' browser={target.IsBrowser} url='{target.Url}'");
                L($"msaa document value: '{Msaa.DocumentUrlAt(user.Current.BoundingRectangle)}'");
                var pwd = FieldFinder.FindPasswordAfter(user);
                L($"password field after login: {(pwd != null ? "found" : "NOT FOUND")}");

                var sender = new KeyboardSender { KeyDelayMs = 6, TargetWindow = hwnd };
                sender.ClearField();
                sender.TypeText("user@example.com");
                FieldFinder.WaitForValue(user, "user@example.com");
                if (pwd != null)
                {
                    FieldFinder.Focus(pwd);
                    sender.ClearField();
                    sender.TypeText("Secr3t-Пароль!");
                }
            });
            await Task.Delay(1000);
            var title = Native.GetWindowTitle(hwnd);
            L($"window title after typing: '{title}'");
            L($"RESULT browser fill: {(title.Contains("U=user@example.com;P=Secr3t-Пароль!") ? "OK" : "FAIL")}");
            lock (events) L("focus watcher events: " + (events.Count == 0 ? "none" : string.Join(", ", events.Distinct())));
        }
        finally
        {
            watcher.Stop();
            try
            {
                // Browsers re-spawn their launcher process: stop the browser processes started by this test
                // (same executable, started after the test began); the user's own browser session is older.
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(browser)))
                {
                    try
                    {
                        if (p.StartTime >= testStarted) p.Kill();
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            await Task.Delay(1500);
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }

    private static IntPtr FindWindow(string titlePart)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (Native.GetWindowTitle(h).Contains(titlePart, StringComparison.Ordinal)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
