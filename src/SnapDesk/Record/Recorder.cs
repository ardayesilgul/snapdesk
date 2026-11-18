using System.Diagnostics;

namespace SnapDesk.Record;

/// <summary>
/// ffmpeg gdigrab ile bölge kaydı. Duraklatma, ayrı segmentler hâlinde
/// kaydedilip sonra birleştirilir; böylece duraklatılan süre videoda görünmez.
/// Ses kaydı bu sürümde yoktur (bkz. README).
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly List<string> _segments = new();
    private readonly Stopwatch _watch = new();
    private string _folder = string.Empty;
    private Rectangle _region;
    private int _fps;
    private bool _cursor;
    private Process? _process;
    private bool _paused;
    private bool _disposed;

    public bool IsRecording => _process is { HasExited: false };
    public bool IsPaused => _paused;
    public TimeSpan Elapsed => _watch.Elapsed;
    public string? LastError { get; private set; }
    public string Folder => _folder;

    public static string? FindFfmpeg()
    {
        var candidates = new[]
        {
            "ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate)) return candidate;
                    continue;
                }

                using var process = Process.Start(new ProcessStartInfo("where.exe", candidate)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process is null) continue;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);

                var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                {
                    return line.Trim();
                }
            }
            catch
            {
                // sonraki adayı dene
            }
        }

        return null;
    }

    public bool Start(Rectangle region, int fps, bool includeCursor)
    {
        LastError = null;

        if (FindFfmpeg() is null)
        {
            LastError = "ffmpeg bulunamadı. Video kaydı için ffmpeg kurun ve PATH'e ekleyin:\n" +
                        "winget install Gyan.FFmpeg";
            return false;
        }

        // H.264 yuv420p çift sayıda boyut ister.
        var width = region.Width & ~1;
        var height = region.Height & ~1;
        if (width < 16 || height < 16)
        {
            LastError = "Kayıt alanı çok küçük (en az 16×16 piksel gerekir).";
            return false;
        }

        _region = new Rectangle(region.Left, region.Top, width, height);
        _fps = Math.Clamp(fps, 5, 60);
        _cursor = includeCursor;
        _folder = Path.Combine(Path.GetTempPath(), "SnapDesk", "rec-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(_folder);

        _watch.Reset();
        _watch.Start();
        _paused = false;

        return StartSegment();
    }

    private bool StartSegment()
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            LastError = "ffmpeg bulunamadı.";
            return false;
        }

        var file = Path.Combine(_folder, $"seg{_segments.Count:000}.mp4");

        var arguments =
            "-y -hide_banner -loglevel error " +
            $"-f gdigrab -framerate {_fps} " +
            $"-offset_x {_region.Left} -offset_y {_region.Top} " +
            $"-video_size {_region.Width}x{_region.Height} " +
            $"-draw_mouse {(_cursor ? 1 : 0)} " +
            "-i desktop " +
            "-c:v libx264 -preset veryfast -crf 22 -pix_fmt yuv420p " +
            $"\"{file}\"";

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                },
            };

            if (!process.Start())
            {
                LastError = "ffmpeg başlatılamadı.";
                return false;
            }

            _process = process;

            // Hata çıktısını biriktir; erken kapanırsa mesaj gösterebilelim.
            lock (_errorLock)
            {
                _errorBuffer = string.Empty;
            }
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (_errorLock)
                    {
                        _errorBuffer += e.Data + Environment.NewLine;
                    }
                }
            };
            process.BeginErrorReadLine();

            _segments.Add(file);
            return true;
        }
        catch (Exception ex)
        {
            LastError = "ffmpeg başlatılamadı: " + ex.Message;
            return false;
        }
    }

    private readonly object _errorLock = new();
    private string _errorBuffer = string.Empty;

    public void Pause()
    {
        if (!IsRecording || _paused)
        {
            return;
        }

        _paused = true;
        _watch.Stop();
        StopSegment();
    }

    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        if (!StartSegment())
        {
            return;
        }

        _paused = false;
        _watch.Start();
    }

    /// <summary>Kaydı bitirir, segmentleri birleştirir ve son dosya yolunu döndürür.</summary>
    public string? Stop()
    {
        if (_paused)
        {
            _paused = false;
            // son segment yok
        }
        else
        {
            StopSegment();
        }

        _watch.Stop();

        if (_segments.Count == 0)
        {
            LastError = "Hiç kayıt alınamadı.";
            return null;
        }

        string errorSnapshot;
        lock (_errorLock)
        {
            errorSnapshot = _errorBuffer;
        }

        if (!string.IsNullOrWhiteSpace(errorSnapshot) && _segments.All(s => !File.Exists(s) || new FileInfo(s).Length == 0))
        {
            LastError = "Kayıt başarısız:\n" + errorSnapshot.Trim();
            return null;
        }

        if (_segments.Count == 1)
        {
            return _segments[0];
        }

        return ConcatSegments();
    }

    private void StopSegment()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("q");
                process.StandardInput.Flush();

                if (!process.WaitForExit(6000))
                {
                    process.Kill(true);
                    process.WaitForExit(2000);
                }
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
                // yoksay
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    private string? ConcatSegments()
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            LastError = "ffmpeg bulunamadı.";
            return null;
        }

        var listPath = Path.Combine(_folder, "list.txt");
        File.WriteAllLines(listPath, _segments.Select(s => $"file '{s.Replace("'", "'\\''")}'"));

        var output = Path.Combine(_folder, "recording.mp4");
        var arguments = $"-y -hide_banner -loglevel error -f concat -safe 0 -i \"{listPath}\" -c copy \"{output}\"";

        try
        {
            using var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                LastError = "Birleştirme başlatılamadı.";
                return null;
            }

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                LastError = "Kayıt birleştirilemedi:\n" + error.Trim();
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            LastError = "Birleştirme hatası: " + ex.Message;
            return null;
        }
    }

    public static async Task<bool> ExportGifAsync(string videoPath, string gifPath)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return false;
        }

        var arguments =
            $"-y -hide_banner -loglevel error -i \"{videoPath}\" " +
            "-vf \"fps=12,scale=iw:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" " +
            $"\"{gifPath}\"";

        try
        {
            using var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(gifPath);
        }
        catch
        {
            return false;
        }
    }

    public void Cancel()
    {
        if (_paused)
        {
            _paused = false;
        }
        else
        {
            StopSegment();
        }

        _watch.Stop();

        foreach (var segment in _segments)
        {
            try
            {
                if (File.Exists(segment)) File.Delete(segment);
            }
            catch
            {
                // yoksay
            }
        }

        _segments.Clear();

        try
        {
            if (Directory.Exists(_folder) && Directory.GetFiles(_folder).Length == 0)
            {
                Directory.Delete(_folder);
            }
        }
        catch
        {
            // yoksay
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process is not null)
        {
            StopSegment();
        }

        _watch.Stop();
    }
}
