using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SnapDesk.Core;

/// <summary>
/// Tek örnek çalışma. İkinci örnek, birinci örneğe komut gönderip kapanır.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _listener;

    public bool IsFirstInstance { get; }

    public event Action<string>? CommandReceived;

    public SingleInstance()
    {
        var user = Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user + "|SnapDesk")))[..12];
        _pipeName = "SnapDesk-" + hash;

        _mutex = new Mutex(initiallyOwned: true, "Global\\SnapDesk-" + hash, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>İlk örnekte çağrılır: komut dinleyicisini başlatır.</summary>
    public void StartListening()
    {
        _cts = new CancellationTokenSource();
        _listener = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var command = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    CommandReceived?.Invoke(command.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SingleInstance listener: " + ex.Message);
                try
                {
                    await Task.Delay(300, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>İkinci örnekte çağrılır: çalışan örneğe komut yollar.</summary>
    public bool SendCommand(string command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("SingleInstance send: " + ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // yoksay
        }

        try
        {
            if (IsFirstInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
            // sahiplenilmemişse yoksay
        }

        _mutex.Dispose();
    }
}
