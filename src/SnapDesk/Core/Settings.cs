using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapDesk.Core;

/// <summary>
/// Kullanıcı ayarları. Dosya: %APPDATA%\SnapDesk\settings.json
/// Varsayılanlar kullanıcı dosyalarına dokunmayacak şekilde seçilmiştir.
/// </summary>
public sealed class Settings
{
    public const string DefaultCaptureHotkey = "Ctrl+1";
    public const string DefaultPinHotkey = "Ctrl+2";
    public const string DefaultLongHotkey = "Ctrl+3";
    public const string DefaultRecordHotkey = "Ctrl+4";

    public string CaptureHotkey { get; set; } = DefaultCaptureHotkey;
    public string PinHotkey { get; set; } = DefaultPinHotkey;
    public string LongCaptureHotkey { get; set; } = DefaultLongHotkey;
    public string RecordHotkey { get; set; } = DefaultRecordHotkey;

    public bool CopyToClipboardOnCapture { get; set; }
    public bool PlayShutterSound { get; set; } = true;
    public bool ShowMagnifier { get; set; } = true;
    public bool ConfirmOnEscape { get; set; } = true;

    /// <summary>Kayıt klasörü. Boşsa resimler klasörü kullanılır.</summary>
    public string SaveFolder { get; set; } = string.Empty;

    public string FileNamePattern { get; set; } = "SnapDesk_{yyyy-MM-dd_HH-mm-ss}";

    /// <summary>png veya jpg.</summary>
    public string ImageFormat { get; set; } = "png";

    public int DefaultStrokeWidth { get; set; } = 4;

    /// <summary>ARGB. Varsayılan kırmızı.</summary>
    public int DefaultColorArgb { get; set; } = unchecked((int)0xFFD84A2F);

    public int DefaultFontSize { get; set; } = 24;

    public string OcrLanguage { get; set; } = string.Empty;

    public bool AutoCopyOcrText { get; set; }

    public int MosaicBlockSize { get; set; } = 12;

    /// <summary>Kayıt kare hızı.</summary>
    public int RecordingFps { get; set; } = 24;

    public bool RecordingIncludeCursor { get; set; } = true;

    /// <summary>Araç çubuğu (Dinamik Ada) düğmeleri.</summary>
    public bool ShowToolbarSerial { get; set; } = false;
    public bool ShowToolbarUndoRedo { get; set; } = true;
    public bool ShowToolbarCrop { get; set; } = true;
    public bool ShowToolbarLongCapture { get; set; } = true;
    public bool ShowToolbarRecord { get; set; } = true;
    public bool ShowToolbarOcr { get; set; } = true;
    public bool ShowToolbarPin { get; set; } = true;

    /// <summary>Windows oturum açılışında sistem tepsisinde hazır başla.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Kayıtta ses kaynağı yok (bkz. README): yalnızca görüntü.</summary>
    public bool FirstRunCompleted { get; set; }

    [JsonIgnore]
    public string Folder { get; private set; } = string.Empty;

    [JsonIgnore]
    public string FilePath => Path.Combine(Folder, "settings.json");

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapDesk");

    public static Settings Load()
    {
        var folder = DefaultFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");

        Settings settings;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
            }
            catch (Exception)
            {
                // Bozuk ayar dosyası uygulamayı kilitlememeli: yedekleyip varsayılana dön.
                try
                {
                    File.Copy(path, path + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                }
                catch
                {
                    // yedekleme başarısız olsa da devam et
                }
                settings = new Settings();
            }
        }
        else
        {
            settings = new Settings();
        }

        settings.Folder = folder;
        settings.Normalize();
        return settings;
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(SaveFolder))
        {
            SaveFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapDesk");
        }

        if (string.IsNullOrWhiteSpace(FileNamePattern))
        {
            FileNamePattern = "SnapDesk_{yyyy-MM-dd_HH-mm-ss}";
        }

        ImageFormat = ImageFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        DefaultStrokeWidth = Math.Clamp(DefaultStrokeWidth, 1, 60);
        DefaultFontSize = Math.Clamp(DefaultFontSize, 8, 200);
        MosaicBlockSize = Math.Clamp(MosaicBlockSize, 4, 80);
        RecordingFps = Math.Clamp(RecordingFps, 5, 60);
        if (string.IsNullOrWhiteSpace(CaptureHotkey)) CaptureHotkey = DefaultCaptureHotkey;
        if (string.IsNullOrWhiteSpace(PinHotkey)) PinHotkey = DefaultPinHotkey;
        if (string.IsNullOrWhiteSpace(LongCaptureHotkey)) LongCaptureHotkey = DefaultLongHotkey;
        if (string.IsNullOrWhiteSpace(RecordHotkey)) RecordHotkey = DefaultRecordHotkey;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(this, JsonOptions);

        // Atomik yazma: yarım kalan dosya ayarları bozmasın.
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, true);
    }

    public string BuildFileName(DateTime time, string extension)
    {
        var pattern = string.IsNullOrWhiteSpace(FileNamePattern)
            ? "SnapDesk_{yyyy-MM-dd_HH-mm-ss}"
            : FileNamePattern;

        // Desen "onek_{tarih-formatı}" biçimindedir; DateTime.ToString tüm
        // harfleri biçim belirteci saydığı için yalnızca {..} içini biçimle.
        var open = pattern.IndexOf('{');
        var close = pattern.LastIndexOf('}');
        string name;
        try
        {
            if (open >= 0 && close > open)
            {
                var prefix = pattern[..open];
                var suffix = pattern[(close + 1)..];
                name = prefix + time.ToString(pattern[(open + 1)..close]) + suffix;
            }
            else
            {
                name = pattern;
            }
        }
        catch (FormatException)
        {
            name = "SnapDesk_" + time.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name + "." + extension;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
