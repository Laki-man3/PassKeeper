namespace PassKeeper.Core.Storage;

public static class FileUtil
{
    /// <summary>Writes a file atomically: temp file + flush + replace. Optionally keeps the previous version.</summary>
    public static void WriteAtomic(string path, byte[] data, string? backupPath = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(data);
            fs.Flush(true);
        }

        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
            if (backupPath != null) File.Copy(path, backupPath, overwrite: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Opens a file that may be locked by another process (e.g. a running browser) and copies it.</summary>
    public static void CopyShared(string source, string destination)
    {
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    public static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PassKeeper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
