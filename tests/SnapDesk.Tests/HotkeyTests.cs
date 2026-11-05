using SnapDesk.Core;

namespace SnapDesk.Tests;

public class HotkeyTests
{
    [Fact]
    public void TryParse_ControlShiftDigit()
    {
        var ok = HotkeyParser.TryParse("ctrl+shift+1", out var modifiers, out var key);

        Assert.True(ok);
        Assert.Equal(Keys.D1, key);
        Assert.True((modifiers & HotkeyParser.ModControl) != 0);
        Assert.True((modifiers & HotkeyParser.ModShift) != 0);
    }

    [Fact]
    public void TryParse_RejectsTextWithoutModifier()
    {
        Assert.False(HotkeyParser.TryParse("1", out _, out _));
        Assert.False(HotkeyParser.TryParse("ctrl+", out _, out _));
        Assert.False(HotkeyParser.TryParse("", out _, out _));
    }

    [Fact]
    public void DefaultSettings_HotkeysAreValid()
    {
        var settings = new Settings();
        var values = new[]
        {
            settings.CaptureHotkey,
            settings.PinHotkey,
            settings.LongCaptureHotkey,
            settings.RecordHotkey,
        };

        foreach (var value in values)
        {
            Assert.True(HotkeyParser.TryParse(value, out _, out _), value);
        }
    }

    [Fact]
    public void Describe_RoundTripsThroughParse()
    {
        Assert.True(HotkeyParser.TryParse("alt+f9", out var modifiers, out var key));

        var text = HotkeyParser.Describe(modifiers, key);

        Assert.True(HotkeyParser.TryParse(text, out var modifiers2, out var key2));
        Assert.Equal(key, key2);
        Assert.Equal(modifiers & (HotkeyParser.ModControl | HotkeyParser.ModAlt | HotkeyParser.ModShift),
            modifiers2 & (HotkeyParser.ModControl | HotkeyParser.ModAlt | HotkeyParser.ModShift));
    }
}
