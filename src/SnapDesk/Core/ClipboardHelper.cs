using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapDesk.Core;

/// <summary>
/// Pano erişimi. Windows panosu başka bir uygulama tarafından kilitliyse
/// sessizce başarısız olmak yerine kısa süre yeniden dener, sonra hata verir.
/// </summary>
public static class ClipboardHelper
{
    public static void CopyImage(Bitmap bitmap)
    {
        var data = new DataObject();

        // Bitmap biçimi eski uygulamalar için, PNG biçimi saydamlık için.
        data.SetData(DataFormats.Bitmap, true, new Bitmap(bitmap));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        data.SetData("PNG", false, stream.ToArray());

        Clipboard.SetDataObject(data, true, 10, 90);
    }

    public static void CopyText(string text)
    {
        Clipboard.SetDataObject(text, true, 10, 90);
    }
}
