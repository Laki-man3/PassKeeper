using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

// packager linux <publishDir> <out.tar.gz> <version> <icon.png>
// packager macos <publishDir> <out.zip> <version> <icon.png>
// Files get Unix modes (executables 0755, the rest 0644): built on Windows, they keep them when unpacked on Linux or macOS.
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
    const string app = "PassKeeper.app/Contents";
    using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
    void Add(string name, byte[] data, bool exec)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        // Unix mode in the upper 16 bits, "regular file" type bits included: macOS's Archive Utility and unzip keep it.
        entry.ExternalAttributes = (int)((0x8000u | (uint)(exec ? Exec : Plain)) << 16);
        using var stream = entry.Open();
        stream.Write(data);
    }
    foreach (var path in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(publish, path).Replace('\\', '/');
        Add($"{app}/MacOS/{relative}", File.ReadAllBytes(path), IsExecutable(relative));
    }
    Add($"{app}/Info.plist", Encoding.UTF8.GetBytes(InfoPlist(version)), false);
    Add($"{app}/Resources/PassKeeper.icns", Icns(File.ReadAllBytes(icon)), false);
    Console.WriteLine($"macos: {output}");
    var main = Path.Combine(publish, "PassKeeper");
    Console.WriteLine("  PassKeeper executable signed: " + (HasCodeSignature(main) ? "yes (ad-hoc)" : "NO"));
    foreach (var dylib in Directory.EnumerateFiles(publish, "*.dylib"))
        if (!HasCodeSignature(dylib)) Console.WriteLine("  not signed: " + Path.GetFileName(dylib));
    return 0;
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

// A Mach-O file (thin, 64-bit) has an LC_CODE_SIGNATURE load command when it is signed, ad-hoc included: Apple Silicon
// runs nothing unsigned.
static bool HasCodeSignature(string path)
{
    if (!File.Exists(path)) return false;
    var data = File.ReadAllBytes(path);
    if (data.Length < 8) return false;
    // A universal file (several architectures): every slice must be signed.
    if (BinaryPrimitives.ReadUInt32BigEndian(data) == 0xCAFEBABE)
    {
        var slices = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        for (var i = 0; i < slices; i++)
        {
            var at = 8 + i * 20;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 12));
            if (!ThinSigned(data.AsSpan(offset, size))) return false;
        }
        return slices > 0;
    }
    return ThinSigned(data);
}

static bool ThinSigned(ReadOnlySpan<byte> data)
{
    if (data.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0xFEEDFACF) return false;
    var count = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
    var offset = 32;
    for (var i = 0; i < count && offset + 8 <= data.Length; i++)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) == 0x1D) return true;
        offset += (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
    }
    return false;
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
      <key>LSMinimumSystemVersion</key><string>14.0</string>
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
