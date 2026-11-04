using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Webignite.Tools.Recording;

// Windows Media Foundation, which comes with Windows: it writes and reads MP4 (H.264 video, AAC sound) without
// anything installed. Only the parts the recorder uses are declared; each interface lists its methods in Windows'
// order, as COM needs, with unused ones as placeholders.

[SupportedOSPlatform("windows")]
internal static class MF
{
    public const int Version = 0x00020070;

    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid Aac = new("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid Pcm = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid AudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid AudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid AudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid AudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid AudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid Duration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    public const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    public const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
    public const int AllStreams = unchecked((int)0xFFFFFFFE);
    public const int MediaSource = unchecked((int)0xFFFFFFFF);
    public const int EndOfStream = 0x2;

    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int length, out IMFMediaBuffer buffer);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);

    public static void Check(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    public static long Pack(int high, int low) => ((long)high << 32) | (uint)low;

    /// <summary>Starts Media Foundation for the work in hand, and stops it after.</summary>
    public static IDisposable Use()
    {
        Check(MFStartup(Version, 0));
        return new Stopper();
    }

    private sealed class Stopper : IDisposable
    {
        public void Dispose()
        {
            // Nothing to do if it fails: the work is done and the process carries on.
            if (MFShutdown() < 0)
                System.Diagnostics.Debug.WriteLine("Media Foundation didn't shut down cleanly.");
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public long Value;

    public static PropVariant Long(long value) => new() { Type = 20 /* VT_I8 */, Value = value };
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int same);
    [PreserveSig] int Compare(IntPtr other, int type, out int same);
    [PreserveSig] int GetUINT32(in Guid key, out int value);
    [PreserveSig] int GetUINT64(in Guid key, out long value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out int length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, int size, out int length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out int length);
    [PreserveSig] int GetBlobSize(in Guid key, out int size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, int size, out int written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out int size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid riid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, int value);
    [PreserveSig] int SetUINT64(in Guid key, long value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, int size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    // IMFAttributes, in order.
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int same);
    [PreserveSig] int Compare(IntPtr other, int type, out int same);
    [PreserveSig] int GetUINT32(in Guid key, out int value);
    [PreserveSig] int GetUINT64(in Guid key, out long value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out int length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, int size, out int length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out int length);
    [PreserveSig] int GetBlobSize(in Guid key, out int size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, int size, out int written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out int size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid riid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, int value);
    [PreserveSig] int SetUINT64(in Guid key, long value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, int size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);

    // IMFMediaType.
    [PreserveSig] int GetMajorType(out Guid type);
    [PreserveSig] int IsCompressedFormat(out int compressed);
    [PreserveSig] int IsEqual(IntPtr other, out int flags);
    [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
    [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // IMFAttributes, in order.
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out int same);
    [PreserveSig] int Compare(IntPtr other, int type, out int same);
    [PreserveSig] int GetUINT32(in Guid key, out int value);
    [PreserveSig] int GetUINT64(in Guid key, out long value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out int length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, int size, out int length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out int length);
    [PreserveSig] int GetBlobSize(in Guid key, out int size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, int size, out int written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out int size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid riid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, int value);
    [PreserveSig] int SetUINT64(in Guid key, long value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, int size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IntPtr destination);

    // IMFSample.
    [PreserveSig] int GetSampleFlags(out int flags);
    [PreserveSig] int SetSampleFlags(int flags);
    [PreserveSig] int GetSampleTime(out long time);
    [PreserveSig] int SetSampleTime(long time);
    [PreserveSig] int GetSampleDuration(out long duration);
    [PreserveSig] int SetSampleDuration(long duration);
    [PreserveSig] int GetBufferCount(out int count);
    [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    [PreserveSig] int RemoveBufferByIndex(int index);
    [PreserveSig] int RemoveAllBuffers();
    [PreserveSig] int GetTotalLength(out int length);
    [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out int length);
    [PreserveSig] int SetCurrentLength(int length);
    [PreserveSig] int GetMaxLength(out int length);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriter
{
    [PreserveSig] int AddStream(IMFMediaType type, out int index);
    [PreserveSig] int SetInputMediaType(int index, IMFMediaType type, IMFAttributes? encoding);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(int index, IMFSample sample);
    [PreserveSig] int SendStreamTick(int index, long time);
    [PreserveSig] int PlaceMarker(int index, IntPtr context);
    [PreserveSig] int NotifyEndOfSegment(int index);
    [PreserveSig] int Flush(int index);
    [PreserveSig] int Finalize_();
    [PreserveSig] int GetServiceForStream(int index, in Guid service, in Guid riid, out IntPtr value);
    [PreserveSig] int GetStatistics(int index, IntPtr statistics);
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(int index, out int selected);
    [PreserveSig] int SetStreamSelection(int index, int selected);
    [PreserveSig] int GetNativeMediaType(int index, int typeIndex, out IMFMediaType type);
    [PreserveSig] int GetCurrentMediaType(int index, out IMFMediaType type);
    [PreserveSig] int SetCurrentMediaType(int index, IntPtr reserved, IMFMediaType type);
    [PreserveSig] int SetCurrentPosition(in Guid format, in PropVariant position);
    [PreserveSig] int ReadSample(int index, int flags, out int actualIndex, out int streamFlags, out long time, out IMFSample? sample);
    [PreserveSig] int Flush(int index);
    [PreserveSig] int GetServiceForStream(int index, in Guid service, in Guid riid, out IntPtr value);
    [PreserveSig] int GetPresentationAttribute(int index, in Guid key, out PropVariant value);
}
