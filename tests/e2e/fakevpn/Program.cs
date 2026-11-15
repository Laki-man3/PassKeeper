// fakevpn <log> <password> [mode] [delay seconds]: a sign-in client for tests.
//   mode "now"    — the sign-in form at once (default)
//   mode "slow"   — the window shows "Loading…" first, the fields appear after the delay (a client that takes its time)
//   mode "dialog" — a main window without fields, then a separate sign-in window after the delay
// Connect with the right password closes the sign-in window (signed in); a wrong one clears the password.
var log = args[0];
var expected = args[1];
var mode = args.Length > 2 ? args[2] : "now";
var delay = args.Length > 3 ? int.Parse(args[3]) : 0;
ApplicationConfiguration.Initialize();

Form SignInForm(string title)
{
    var form = new Form { Text = title, Width = 380, Height = 220, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
    var user = new TextBox { Left = 120, Top = 20, Width = 220, AccessibleName = "Username" };
    var pass = new TextBox { Left = 120, Top = 60, Width = 220, UseSystemPasswordChar = true, AccessibleName = "Password" };
    var go = new Button { Text = "Connect", Left = 120, Top = 105, Width = 120 };
    form.Controls.AddRange([new Label { Text = "Username:", Left = 20, Top = 22, AutoSize = true }, user, new Label { Text = "Password:", Left = 20, Top = 62, AutoSize = true }, pass, go]);
    form.AcceptButton = go;
    go.Click += (_, _) =>
    {
        var ok = pass.Text == expected;
        File.AppendAllText(log, $"submit user={user.Text} ok={ok}{Environment.NewLine}");
        if (ok) form.Close();
        else pass.Clear();
    };
    return form;
}

if (mode == "now")
{
    Application.Run(SignInForm("FakeVPN Sign In"));
    return;
}

if (mode == "slow")
{
    var form = SignInForm("FakeVPN Sign In");
    var parts = form.Controls.Cast<Control>().ToList();
    foreach (var c in parts) c.Visible = false;
    var loading = new Label { Text = "Loading…", Left = 20, Top = 60, AutoSize = true };
    form.Controls.Add(loading);
    var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, delay) * 1000 };
    timer.Tick += (_, _) =>
    {
        timer.Stop();
        // The fields just appear: no focus event tells about them (some clients send none).
        loading.Visible = false;
        foreach (var c in parts) c.Visible = true;
    };
    form.Shown += (_, _) => timer.Start();
    Application.Run(form);
    return;
}

// "dialog": the main window first, the sign-in window later (and the main window stays).
var main = new Form { Text = "FakeVPN", Width = 420, Height = 260, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
main.Controls.Add(new Label { Text = "Contacting server…", Left = 20, Top = 20, AutoSize = true });
var later = new System.Windows.Forms.Timer { Interval = Math.Max(1, delay) * 1000 };
later.Tick += (_, _) =>
{
    later.Stop();
    var dialog = SignInForm("FakeVPN Sign In");
    dialog.FormClosed += (_, _) => main.Close();
    dialog.Show();
    dialog.Activate();
};
main.Shown += (_, _) => later.Start();
Application.Run(main);
