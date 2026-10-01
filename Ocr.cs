using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Traduce
{
    internal sealed class OcrImageTooLargeException : Exception { }
    internal sealed class OcrUnavailableException : Exception { }

    internal static class RegionInput
    {
        internal static async Task<TranslationInput> Choose(ImageInput image, Func<CancellationToken, Task<string>> read, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            string text = null, reason = "no_text";
            try { text = await read(token); }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); reason = "timeout"; }
            catch (OcrImageTooLargeException) { reason = "image_too_large"; }
            catch (OcrUnavailableException) { reason = "unavailable"; }
            catch (Exception) { reason = "ocr_error"; }
            token.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(text)
                ? new TranslationInput { Image = image, OcrReason = reason, OcrMilliseconds = watch.ElapsedMilliseconds }
                : new TranslationInput { Text = text.Trim(), OcrReason = "text_recognized", OcrMilliseconds = watch.ElapsedMilliseconds };
        }

        public static Task<TranslationInput> FromImage(ImageInput image, CancellationToken token)
        {
            // WinRT initialization and PNG decoding never block the UI thread.
            return Choose(image, cancellation => Task.Run(() => LocalOcr.Read(image.Png, cancellation), cancellation), token);
        }
    }

    internal static class LocalOcr
    {
        public static async Task<string> Read(byte[] png, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var memory = new InMemoryRandomAccessStream())
            {
                timeout.CancelAfter(3000);
                token = timeout.Token;
                using (var writer = new DataWriter(memory.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync().AsTask(token);
                }
                var decoder = await BitmapDecoder.CreateAsync(memory).AsTask(token);
                if (decoder.PixelWidth > OcrEngine.MaxImageDimension || decoder.PixelHeight > OcrEngine.MaxImageDimension) throw new OcrImageTooLargeException();
                using (var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token))
                {
                    var preferred = OcrEngine.TryCreateFromUserProfileLanguages();
                    bool attempted = false;
                    if (preferred != null)
                    {
                        attempted = true;
                        string text = await Recognize(preferred, bitmap, token);
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                    foreach (var language in OcrEngine.AvailableRecognizerLanguages)
                    {
                        if (preferred != null && language.LanguageTag == preferred.RecognizerLanguage.LanguageTag) continue;
                        var engine = OcrEngine.TryCreateFromLanguage(language);
                        if (engine == null) continue;
                        attempted = true;
                        string text = await Recognize(engine, bitmap, token);
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                    if (!attempted) throw new OcrUnavailableException();
                    return null;
                }
            }
        }

        private static async Task<string> Recognize(OcrEngine engine, SoftwareBitmap bitmap, CancellationToken token)
        {
            var result = await engine.RecognizeAsync(bitmap).AsTask(token);
            return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
        }
    }
}
