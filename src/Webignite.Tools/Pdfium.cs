using System.Runtime.InteropServices;

namespace Webignite.Tools;

/// <summary>
/// PDFium, Google's PDF library (not a browser), called directly. The library itself comes from the bblanchon
/// PDFium packages, which follow Chromium's PDFium closely, so fixes for files made to attack PDF readers arrive
/// with each update. Callers hold <see cref="PdfPages.Gate"/>: PDFium isn't safe to use from two threads at once.
/// </summary>
internal static class Pdfium
{
    private const string Library = "pdfium";
    private static bool started;

    /// <summary>Starts PDFium the first time it's needed.</summary>
    public static void Start()
    {
        if (started)
            return;
        FPDF_InitLibrary();
        started = true;
    }

    /// <summary>
    /// Opens a PDF from memory. PDFium reads the copy for as long as the document is open, so it lives with it.
    /// A file that isn't a PDF, is damaged or wants a password throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static Document Open(byte[] file)
    {
        Start();
        var memory = Marshal.AllocHGlobal(Math.Max(1, file.Length));
        Marshal.Copy(file, 0, memory, file.Length);
        var handle = FPDF_LoadMemDocument(memory, file.Length, null);
        if (handle == IntPtr.Zero)
        {
            var error = FPDF_GetLastError();
            Marshal.FreeHGlobal(memory);
            throw new InvalidDataException(error == 4
                ? "The PDF is locked with a password."
                : "It isn't a PDF that can be read. It may be damaged.");
        }
        return new Document(handle, memory);
    }

    /// <summary>An open PDF and the memory it reads from; both go when it's disposed.</summary>
    public sealed class Document(IntPtr handle, IntPtr memory) : IDisposable
    {
        public IntPtr Handle => handle;
        public int PageCount => FPDF_GetPageCount(handle);

        public void Dispose()
        {
            FPDF_CloseDocument(handle);
            if (memory != IntPtr.Zero)
                Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>
    /// One page drawn on white as four bytes a pixel (blue, green, red, alpha), as big as fits in the box given,
    /// keeping its shape and any turn it has.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Draw(Document document, int index, int boxWidth, int boxHeight)
    {
        var page = FPDF_LoadPage(document.Handle, index);
        if (page == IntPtr.Zero)
            throw new InvalidDataException($"Page {index + 1} couldn't be read.");
        try
        {
            var (pageWidth, pageHeight) = (FPDF_GetPageWidthF(page), FPDF_GetPageHeightF(page));
            var scale = Math.Min(boxWidth / pageWidth, boxHeight / pageHeight);
            var width = Math.Max(1, (int)Math.Round(pageWidth * scale));
            var height = Math.Max(1, (int)Math.Round(pageHeight * scale));
            var bitmap = FPDFBitmap_Create(width, height, 1);
            if (bitmap == IntPtr.Zero)
                throw new InvalidDataException("The page is too big to draw.");
            try
            {
                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, RenderAnnotations | RenderLcdText);
                var stride = FPDFBitmap_GetStride(bitmap);
                var buffer = FPDFBitmap_GetBuffer(bitmap);
                var pixels = new byte[width * height * 4];
                for (var row = 0; row < height; row++)
                    Marshal.Copy(buffer + row * stride, pixels, row * width * 4, width * 4);
                return (pixels, width, height);
            }
            finally
            {
                FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            FPDF_ClosePage(page);
        }
    }

    /// <summary>The words on a page, in reading order as PDFium finds them. A scanned page has none.</summary>
    public static string Text(Document document, int index)
    {
        var page = FPDF_LoadPage(document.Handle, index);
        if (page == IntPtr.Zero)
            return "";
        var textPage = FPDFText_LoadPage(page);
        try
        {
            if (textPage == IntPtr.Zero)
                return "";
            var count = FPDFText_CountChars(textPage);
            if (count <= 0)
                return "";
            var buffer = new ushort[count + 1];
            var written = FPDFText_GetText(textPage, 0, count, buffer);
            var chars = new char[Math.Max(0, written - 1)];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = (char)buffer[i];
            return new string(chars);
        }
        finally
        {
            if (textPage != IntPtr.Zero)
                FPDFText_ClosePage(textPage);
            FPDF_ClosePage(page);
        }
    }

    /// <summary>
    /// A JPEG put on a page as it is, without decoding it again (so it stays small), filling the box of
    /// <paramref name="width"/> by <paramref name="height"/> points at the page's bottom left.
    /// </summary>
    public static void PutJpeg(IntPtr document, IntPtr page, byte[] jpeg, double width, double height)
    {
        var image = FPDFPageObj_NewImageObj(document);
        var pinned = GCHandle.Alloc(jpeg, GCHandleType.Pinned);
        try
        {
            GetBlock read = (param, position, buffer, size) =>
            {
                var (from, length) = ((long)position.Value, (long)size.Value);
                if (from < 0 || length < 0 || from + length > jpeg.Length)
                    return 0;
                Marshal.Copy(jpeg, (int)from, buffer, (int)length);
                return 1;
            };
            var access = new FileAccess { FileLength = new CULong((nuint)jpeg.Length), GetBlock = Marshal.GetFunctionPointerForDelegate(read) };
            if (!FPDFImageObj_LoadJpegFileInline([page], 1, image, ref access))
                throw new IOException("A picture couldn't be put on the page.");
            GC.KeepAlive(read);
        }
        finally
        {
            pinned.Free();
        }
        FPDFImageObj_SetMatrix(image, width, 0, 0, height, 0, 0);
        FPDFPage_InsertObject(page, image);
        FPDFPage_GenerateContent(page);
    }

    // Saving: PDFium hands the file over in pieces to a callback, which keeps them.
    [ThreadStatic] private static MemoryStream? saving;

    private static readonly WriteBlock Writer = (_, data, size) =>
    {
        var piece = new byte[size];
        Marshal.Copy(data, piece, 0, (int)size);
        saving!.Write(piece);
        return 1;
    };

    public static byte[] Save(IntPtr document)
    {
        saving = new MemoryStream();
        try
        {
            var write = new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(Writer) };
            if (!FPDF_SaveAsCopy(document, ref write, 0))
                throw new IOException("The PDF couldn't be saved.");
            return saving.ToArray();
        }
        finally
        {
            saving = null;
        }
    }

    private const int RenderAnnotations = 0x01;
    private const int RenderLcdText = 0x02;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int WriteBlock(IntPtr self, IntPtr data, uint size);

    [StructLayout(LayoutKind.Sequential)]
    public struct FileWrite
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    // C's unsigned long: 32 bits on Windows, 64 on Linux and macOS, hence CULong.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBlock(IntPtr param, CULong position, IntPtr buffer, CULong size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAccess
    {
        public CULong FileLength;
        public IntPtr GetBlock;
        public IntPtr Param;
    }

    [DllImport(Library)] private static extern void FPDF_InitLibrary();
    [DllImport(Library)] private static extern uint FPDF_GetLastError();
    [DllImport(Library)] public static extern IntPtr FPDF_CreateNewDocument();
    [DllImport(Library, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, [MarshalAs(UnmanagedType.LPStr)] string? password);
    [DllImport(Library)] public static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Library)] public static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Library)] public static extern int FPDF_GetPageSizeByIndex(IntPtr document, int index, out double width, out double height);
    [DllImport(Library, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_ImportPages(IntPtr target, IntPtr source, [MarshalAs(UnmanagedType.LPStr)] string? pages, int index);
    [DllImport(Library)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Library)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Library)] private static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Library)] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Library)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport(Library)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Library)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Library)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Library)] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Library)] private static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Library)] private static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Library)] private static extern int FPDFText_GetText(IntPtr textPage, int start, int count, [Out] ushort[] result);
    [DllImport(Library)] public static extern IntPtr FPDFPage_New(IntPtr document, int index, double width, double height);
    [DllImport(Library)] public static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(Library)] public static extern void FPDFPage_SetRotation(IntPtr page, int rotation);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);
    [DllImport(Library)] public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr item);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPage_GenerateContent(IntPtr page);
    [DllImport(Library)] public static extern IntPtr FPDFPageObj_NewImageObj(IntPtr document);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FPDFImageObj_LoadJpegFileInline(IntPtr[] pages, int count, IntPtr image, ref FileAccess file);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFImageObj_SetBitmap(IntPtr[] pages, int count, IntPtr image, IntPtr bitmap);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFImageObj_SetMatrix(IntPtr image, double a, double b, double c, double d, double e, double f);
    [DllImport(Library)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
    [DllImport(Library)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Library, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)] public static extern IntPtr FPDFText_LoadStandardFont(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string font);
    [DllImport(Library)] public static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float size);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFText_SetText(IntPtr text, IntPtr wideText);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetFillColor(IntPtr item, uint r, uint g, uint b, uint a);
    [DllImport(Library)] public static extern void FPDFPageObj_Transform(IntPtr item, double a, double b, double c, double d, double e, double f);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FPDF_SaveAsCopy(IntPtr document, ref FileWrite write, uint flags);
}
