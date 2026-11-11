using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Tools;

/// <summary>
/// Reads the words in a picture with the text reading built into Windows (Windows.Media.Ocr), in the languages
/// the person has set up. Nothing is installed with the app and nothing leaves the PC. Windows offers this only
/// to its newer kind of program, so it is called here by hand, through the tables of functions those objects
/// carry; the numbers below are each function's place in its table, taken from Windows' own definitions.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsOcr
{
    private static readonly Guid OcrEngineStatics = new("5bffa85a-3384-3540-9940-699120d428a8");
    private static readonly Guid SoftwareBitmapStatics = new("df0385db-672f-4a9d-806e-c2442f343e86");
    private static readonly Guid CryptographicBufferStatics = new("320b7e22-3cb0-4cdf-8663-1d28910065eb");
    private static readonly Guid AsyncInfo = new("00000036-0000-0000-c000-000000000046");

    private const int Bgra8 = 87;

    /// <summary>True when Windows has a reader for one of the person's languages.</summary>
    public static bool Available()
    {
        try
        {
            var engine = Engine(out _);
            Release(engine);
            return engine != IntPtr.Zero;
        }
#pragma warning disable CA1031 // Any failure to reach Windows' reader means the same thing: it can't be used.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>
    /// The words in a picture given as BGRA pixels, top row first, one line of the picture per line of text.
    /// Null when Windows has no reader for the person's languages; empty when it found no words.
    /// </summary>
    public static async Task<string?> ReadAsync(byte[] bgra, int width, int height)
    {
        IntPtr engine = Engine(out var most), bitmap = IntPtr.Zero, reading = IntPtr.Zero, info = IntPtr.Zero, result = IntPtr.Zero;
        if (engine == IntPtr.Zero)
            return null;
        try
        {
            (bgra, width, height) = Sized(bgra, width, height, most);
            bitmap = Bitmap(bgra, width, height);
            reading = Start(engine, bitmap);
            info = As(reading, AsyncInfo);
            // 0 while it is still reading, 1 when done, anything else a failure.
            for (var waited = 0; ; waited += 15)
            {
                var status = Status(info);
                if (status == 1)
                    break;
                if (status != 0 || waited > 20_000)
                    throw new InvalidOperationException("Windows couldn't read the picture.");
                await Task.Delay(15);
            }
            return Lines(reading, ref result);
        }
        finally
        {
            Release(result);
            Release(info);
            Release(reading);
            Release(bitmap);
            Release(engine);
        }
    }

    // IOcrEngine.RecognizeAsync, place 6.
    private static unsafe IntPtr Start(IntPtr engine, IntPtr bitmap)
    {
        IntPtr reading;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(engine, 6))(engine, bitmap, &reading));
        return reading;
    }

    // IAsyncInfo.get_Status, place 7.
    private static unsafe int Status(IntPtr info)
    {
        int status;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(info, 7))(info, &status));
        return status;
    }

    // IAsyncOperation.GetResults, place 8, then each line's words.
    private static unsafe string Lines(IntPtr reading, ref IntPtr result)
    {
        IntPtr found;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(reading, 8))(reading, &found));
        result = found;
        // IOcrResult.get_Lines, place 6: a list whose places are GetAt (6) and get_Size (7).
        IntPtr lines;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(found, 6))(found, &lines));
        try
        {
            uint count;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(lines, 7))(lines, &count));
            var text = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                IntPtr line;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(lines, 6))(lines, i, &line));
                try
                {
                    // IOcrLine.get_Text, place 7.
                    IntPtr words;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(line, 7))(line, &words));
                    text.Add(Text(words));
                }
                finally
                {
                    Release(line);
                }
            }
            return ScreenText.Tidy(text);
        }
        finally
        {
            Release(lines);
        }
    }

    // OcrEngine.TryCreateFromUserProfileLanguages (place 10), and the largest picture it takes (place 6).
    private static unsafe IntPtr Engine(out int most)
    {
        var statics = Factory("Windows.Media.Ocr.OcrEngine", OcrEngineStatics);
        try
        {
            uint largest;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(statics, 6))(statics, &largest));
            most = (int)Math.Min(largest, int.MaxValue);
            IntPtr engine;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(statics, 10))(statics, &engine));
            return engine;
        }
        finally
        {
            Release(statics);
        }
    }

    // The pixels wrapped as Windows' own kind of buffer (CryptographicBuffer.CreateFromByteArray, place 9), then
    // copied into its own kind of picture (SoftwareBitmap.CreateCopyFromBuffer, place 9).
    private static unsafe IntPtr Bitmap(byte[] bgra, int width, int height)
    {
        IntPtr buffers = Factory("Windows.Security.Cryptography.CryptographicBuffer", CryptographicBufferStatics), buffer = IntPtr.Zero;
        var bitmaps = Factory("Windows.Graphics.Imaging.SoftwareBitmap", SoftwareBitmapStatics);
        try
        {
            fixed (byte* pixels = bgra)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, byte*, IntPtr*, int>)Slot(buffers, 9))(buffers, (uint)bgra.Length, pixels, &buffer));
            IntPtr bitmap;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, int, IntPtr*, int>)Slot(bitmaps, 9))(bitmaps, buffer, Bgra8, width, height, &bitmap));
            return bitmap;
        }
        finally
        {
            Release(buffer);
            Release(bitmaps);
            Release(buffers);
        }
    }

    // Small print reads far better enlarged; a picture bigger than Windows takes is shrunk to fit.
    private static unsafe (byte[] Bgra, int Width, int Height) Sized(byte[] bgra, int width, int height, int most)
    {
        var enlarge = ScreenText.Enlarge(width, height, most);
        var shrink = Math.Min(1.0, Math.Min((double)most / width, (double)most / height));
        if (enlarge == 1 && shrink >= 1)
            return (bgra, width, height);
        var (newWidth, newHeight) = enlarge > 1 ? (width * enlarge, height * enlarge) : (Math.Max(1, (int)(width * shrink)), Math.Max(1, (int)(height * shrink)));
        var from = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var to = new SKImageInfo(newWidth, newHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var source = new SKBitmap();
        fixed (byte* pixels = bgra)
        {
            source.InstallPixels(from, (IntPtr)pixels, from.RowBytes);
            using var resized = source.Resize(to, new SKSamplingOptions(SKCubicResampler.Mitchell));
            return (resized.Bytes, newWidth, newHeight);
        }
    }

    private static unsafe void** Slot(IntPtr thing, int place) => (void**)(*(void***)thing)[place];

    private static IntPtr As(IntPtr thing, Guid kind)
    {
        Check(Marshal.QueryInterface(thing, in kind, out var found));
        return found;
    }

    private static void Release(IntPtr thing)
    {
        if (thing != IntPtr.Zero)
            Marshal.Release(thing);
    }

    private static void Check(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    private static IntPtr Factory(string className, Guid kind)
    {
        Check(WindowsCreateString(className, (uint)className.Length, out var name));
        try
        {
            Check(RoGetActivationFactory(name, in kind, out var factory));
            return factory;
        }
        finally
        {
            _ = WindowsDeleteString(name);
        }
    }

    private static unsafe string Text(IntPtr words)
    {
        try
        {
            uint length;
            var characters = WindowsGetStringRawBuffer(words, &length);
            return characters is null ? "" : new string(characters, 0, (int)length);
        }
        finally
        {
            _ = WindowsDeleteString(words);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string source, uint length, out IntPtr text);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr text);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern unsafe char* WindowsGetStringRawBuffer(IntPtr text, uint* length);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr className, in Guid kind, out IntPtr factory);
}
