using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SnapDesk.Core;

/// <summary>
/// Tek örnek çalışma. İkinci örnek, birinci örneğe komut gönderip kapanır.
/// Farklı masaüstü bağlamları (örn. izole araç masaüstleri) birbirini kilitlemesin diye
/// masaüstü adı da anahtara katılır.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int UoiName = 2;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _listener;

    public bool IsFirstInstance { get; }

    public event Action<string>? CommandReceived;

    public SingleInstance() : this(desktopName: null)
    {
    }

    public SingleInstance(string? desktopName)
    {
        var user = Environment.UserName;
        var desktop = string.IsNullOrWhiteSpace(desktopName) ? GetCurrentDesktopName() : desktopName.Trim();
        _pipeName = BuildPipeName(user, desktop);

        _mutex = new Mutex(initiallyOwned: true, "Global\\" + _pipeName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public static string BuildPipeName(string user, string desktop)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{user}|{desktop}|SnapDesk")))[..12];
        return "SnapDesk-" + hash;
    }

    public static string GetCurrentDesktopName()
    {
        try
        {
            var desktop = GetThreadDesktop(GetCurrentThreadId());
            if (desktop != IntPtr.Zero)
            {
                var buffer = new char[256];
                if (GetUserObjectInformation(desktop, UoiName, buffer, buffer.Length * sizeof(char), out var bytes) && bytes > 0)
                {
                    var len = Array.IndexOf(buffer, '\0');
                    var name = len >= 0 ? new string(buffer, 0, len) : new string(buffer);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch
        {
            // Masaüstü adı okunamazsa varsayılana düş.
        }

        return "Default";
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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, [Out] char[] pvInfo, int nLength, out int lpnLengthNeeded);
}
