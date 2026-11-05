using SnapDesk.Core;

namespace SnapDesk.Tests;

public class SettingsFileNameTests
{
    [Fact]
    public void BuildFileName_UsesPatternDateToken()
    {
        var settings = new Settings { FileNamePattern = "SnapDesk_{yyyy-MM-dd_HH-mm-ss}" };

        var name = settings.BuildFileName(new DateTime(2026, 10, 22, 19, 24, 15), "png");

        Assert.Equal("SnapDesk_2026-10-22_19-24-15.png", name);
    }

    [Fact]
    public void BuildFileName_KeepsLiteralLettersOutsideToken()
    {
        // Eskiden "SnapDesk_" içindeki s/k harfleri tarih biçimi sanılıyordu.
        var settings = new Settings { FileNamePattern = "SnapDesk_{HH-mm-ss}" };

        var name = settings.BuildFileName(new DateTime(2026, 10, 22, 8, 5, 9), "mp4");

        Assert.Equal("SnapDesk_08-05-09.mp4", name);
    }

    [Fact]
    public void BuildFileName_ReplacesInvalidCharacters()
    {
        var settings = new Settings { FileNamePattern = "ekran:_{yyyy}" };

        var name = settings.BuildFileName(new DateTime(2026, 1, 1), "png");

        Assert.Equal("ekran__2026.png", name);
    }

    [Fact]
    public void BuildFileName_WithoutToken_UsedAsIs()
    {
        var settings = new Settings { FileNamePattern = "sabit-ad" };

        Assert.Equal("sabit-ad.png", settings.BuildFileName(DateTime.Now, "png"));
    }
}
