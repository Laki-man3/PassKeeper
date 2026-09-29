using System.IO;
using System.IO.Pipes;
using PassKeeper.Shared;

namespace PassKeeper.Services;

/// <summary>
/// One instance per user session and vault. Later launches forward a command ("SHOW", "EXIT") over a named pipe
/// restricted to the current user. A copy started with its own data folder (--data, a second vault) is a separate
/// instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _name = name;
    }

    /// <summary>Pipe / mutex name: the default one for the standard data folder, a suffixed one for --data.</summary>
    public static string NameFor(string? dataDirectory)
    {
        var name = InstallLayout.PipeName();
        if (string.IsNullOrWhiteSpace(dataDirectory)) return name;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).TrimEnd('\\').ToLowerInvariant()));
        return name + "-" + Convert.ToHexString(bytes, 0, 4);
    }

    public static SingleInstance? TryAcquire(string? dataDirectory = null)
    {
        var name = NameFor(dataDirectory);
        var mutex = new Mutex(true, "Local\\" + name, out var created);
        if (created) return new SingleInstance(mutex, name);
        mutex.Dispose();
        return null;
    }

    public static bool Send(string command, int timeoutMs = 2000, string? dataDirectory = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", NameFor(dataDirectory), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Listen(Action<string> onCommand)
    {
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrWhiteSpace(line)) onCommand(line.Trim().ToUpperInvariant());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(200, CancellationToken.None);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
