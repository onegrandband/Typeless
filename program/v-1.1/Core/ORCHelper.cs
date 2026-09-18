using System.Drawing.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Drawing;
using System.Diagnostics;

namespace AutoTyper.Core;

public static class OCRHelper
{
    public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
    {
        try
        {
            var ocrEngine = OcrEngine.TryCreateFromLanguage(new Language("en"))
                ?? OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();

            if (ocrEngine == null)
                return "OCR engine not available. Ensure Windows OCR language pack is installed.";

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var ras = await SoftwareBitmap.CreateCopyFromStreamAsync(stream.AsRandomAccessStream());
            var result = await ocrEngine.RecognizeAsync(ras);
            return result.Text;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("OCR Error: " + ex.Message);
            return string.Empty;
        }
    }
}
