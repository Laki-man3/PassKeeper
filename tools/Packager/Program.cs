using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

// packager linux <publishDir> <out.tar.gz> <version> <icon.png>
// packager macos <publishDir> <out.zip> <version> <icon.png>
// Linux tar entries get Unix modes on any host. macOS bundles require native codesign/ditto finalization.
const UnixFileMode Exec = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
    | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
const UnixFileMode Plain = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

if (args.Length < 5) return Fail("usage: packager linux|macos <publishDir> <out> <version> <icon.png>");
var (kind, publish, output, version, icon) = (args[0], args[1], args[2], args[3], args[4]);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
if (File.Exists(output)) File.Delete(output);
return kind switch
{
    "linux" => Linux(),
    "macos" => MacOs(),
    _ => Fail("unknown kind " + kind),
};

int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static bool IsExecutable(string relative) =>
    Path.GetFileName(relative) is "PassKeeper" or "install.sh" or "createdump" || relative.EndsWith(".so", StringComparison.Ordinal)
    || relative.EndsWith(".dylib", StringComparison.Ordinal);

int Linux()
{
    var root = $"PassKeeper-{version}";
    using var file = File.Create(output);
    using var gzip = new GZipStream(file, CompressionLevel.Optimal);
    using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
    void Dir(string name) => tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name + "/") { Mode = Exec });
    void Add(string name, byte[] data, bool exec)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { Mode = exec ? Exec : Plain, DataStream = new MemoryStream(data) };
        tar.WriteEntry(entry);
    }
    Dir(root);
    foreach (var path in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(publish, path).Replace('\\', '/');
        Add($"{root}/{relative}", File.ReadAllBytes(path), IsExecutable(relative));
    }
    Add($"{root}/passkeeper.png", File.ReadAllBytes(icon), false);
    Add($"{root}/install.sh", Encoding.UTF8.GetBytes(InstallScript.Replace("\r\n", "\n")), true);
    Console.WriteLine($"linux: {output}");
    return 0;
}

int MacOs()
{
    if (!OperatingSystem.IsMacOS())
        return Fail("macOS packages must be finalized on macOS: codesign and ditto are required.");

    var staging = Path.Combine(Path.GetTempPath(), "PassKeeper-package-" + Guid.NewGuid().ToString("N"));
    var app = Path.Combine(staging, "PassKeeper.app");
    var contents = Path.Combine(app, "Contents");
    var macos = Path.Combine(contents, "MacOS");
    var resources = Path.Combine(contents, "Resources");
    var runtime = Path.Combine(resources, "runtime");
    try
    {
        Directory.CreateDirectory(macos);
        Directory.CreateDirectory(runtime);
        foreach (var path in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(publish, path);
            var destination = relative == "PassKeeper" ? Path.Combine(macos, relative) : Path.Combine(runtime, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
            File.SetUnixFileMode(destination, IsExecutable(relative) ? Exec : Plain);
        }
        // apphost resolves the managed entry point's symlink and discovers the self-contained runtime beside it.
        // Keep data/managed assemblies out of MacOS: codesign otherwise treats them as nested executable code
        // and puts their signatures in extended attributes, which ordinary ZIP writers discard.
        File.CreateSymbolicLink(Path.Combine(macos, "PassKeeper.dll"), "../Resources/runtime/PassKeeper.dll");
        File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(version), new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(resources, "PassKeeper.icns"), Icns(File.ReadAllBytes(icon)));

        foreach (var path in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
            if (path.EndsWith(".dylib", StringComparison.Ordinal) || Path.GetFileName(path) == "createdump")
                Run("/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", path);
        // Sign the completed bundle, not just apphost: Info.plist and resources must be sealed together.
        Run("/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", app);
        Run("/usr/bin/codesign", "--verify", "--deep", "--strict", app);
        Run("/usr/bin/ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, Path.GetFullPath(output));
        Console.WriteLine($"macos (ad-hoc signed, not notarized): {output}");
        return 0;
    }
    finally
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
    }
}

static void Run(string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException($"{executable} failed (exit code {process.ExitCode}).");
}

// An .icns with one 256x256 PNG image ("ic08"): macOS scales it for the Dock and Finder.
static byte[] Icns(byte[] png)
{
    var result = new byte[8 + 8 + png.Length];
    Encoding.ASCII.GetBytes("icns").CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)result.Length);
    Encoding.ASCII.GetBytes("ic08").CopyTo(result, 8);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), (uint)(8 + png.Length));
    png.CopyTo(result, 16);
    return result;
}

static string InfoPlist(string version) => $"""
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0">
    <dict>
      <key>CFBundleName</key><string>PassKeeper</string>
      <key>CFBundleDisplayName</key><string>PassKeeper</string>
      <key>CFBundleIdentifier</key><string>io.github.laki-man3.passkeeper</string>
      <key>CFBundleVersion</key><string>{version}</string>
      <key>CFBundleShortVersionString</key><string>{version}</string>
      <key>CFBundlePackageType</key><string>APPL</string>
      <key>CFBundleExecutable</key><string>PassKeeper</string>
      <key>CFBundleIconFile</key><string>PassKeeper</string>
      <key>LSMinimumSystemVersion</key><string>15.0</string>
      <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
      <key>NSHighResolutionCapable</key><true/>
    </dict>
    </plist>
    """;

partial class Program
{
    private const string InstallScript = """
        #!/bin/sh
        # Installs PassKeeper for the current user: ~/.local/opt/PassKeeper and a menu entry. No root rights needed.
        set -e
        here="$(cd "$(dirname "$0")" && pwd)"
        target="$HOME/.local/opt/PassKeeper"
        mkdir -p "$target" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
        cp -R "$here"/. "$target"/
        rm -f "$target/install.sh"
        chmod +x "$target/PassKeeper"
        cp "$here/passkeeper.png" "$HOME/.local/share/icons/hicolor/256x256/apps/passkeeper.png"
        cat > "$HOME/.local/share/applications/passkeeper.desktop" <<EOF
        [Desktop Entry]
        Type=Application
        Name=PassKeeper
        Comment=Offline password manager
        Exec="$target/PassKeeper"
        Icon=passkeeper
        Terminal=false
        Categories=Utility;Security;
        EOF
        echo "PassKeeper is installed in $target and added to the applications menu."
        """;
}
