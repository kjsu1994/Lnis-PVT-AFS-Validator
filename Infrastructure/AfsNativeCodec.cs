using System.Runtime.InteropServices;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

public sealed class AfsNativeCodec : IAfsFrameCodec
{
    private const string Library = "LnisAfsCodec";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public AfsNativeCodec()
    {
        var version = Native.GetAbiVersion();
        if (version != 1) throw new NotSupportedException($"Unsupported LnisAfsCodec ABI {version}.");
    }

    public async Task<byte[]> EncodeAsync(int toi, ReadOnlyMemory<byte> sb2Bits, ReadOnlyMemory<byte> sb3Bits, ReadOnlyMemory<byte> sb4Bits, CancellationToken token)
    {
        Validate(toi, sb2Bits, sb3Bits, sb4Bits); await Gate.WaitAsync(token);
        try
        {
            var frame = new byte[750]; var a = sb2Bits.ToArray(); var b = sb3Bits.ToArray(); var c = sb4Bits.ToArray();
            var result = Native.Encode((byte)toi, a, (uint)a.Length, b, (uint)b.Length, c, (uint)c.Length, frame, (uint)frame.Length);
            if (result != 0) throw Error("encode", result); return frame;
        }
        finally { Gate.Release(); }
    }

    public async Task<AfsDecodedFrame> DecodeAsync(int toi, ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        if (toi is < 0 or > 99 || frame.Length != 750) throw new ArgumentException("Invalid AFS frame or TOI.");
        await Gate.WaitAsync(token);
        try
        {
            var sb2 = new byte[1176]; var sb3 = new byte[846]; var sb4 = new byte[846];
            var result = Native.Decode((byte)toi, frame.ToArray(), (uint)frame.Length, sb2, 1176, sb3, 846, sb4, 846, out var status);
            if (result < 0) throw Error("decode", result);
            return new(sb2, sb3, sb4, status.Sb2Ok != 0, status.Sb3Ok != 0, status.Sb4Ok != 0,
                status.Sb2Corrections, status.Sb3Corrections, status.Sb4Corrections);
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private static void Validate(int toi, params ReadOnlyMemory<byte>[] blocks)
    {
        if (toi is < 0 or > 99 || blocks[0].Length != 1176 || blocks[1].Length != 846 || blocks[2].Length != 846) throw new ArgumentException("Invalid AFS block length or TOI.");
        if (blocks.Any(x => x.Span.IndexOfAnyExcept((byte)0, (byte)1) >= 0)) throw new ArgumentException("AFS inputs must contain unpacked 0/1 bits.");
    }
    private static Exception Error(string operation, int code)
    {
        var pointer = Native.GetLastError(); var message = pointer == IntPtr.Zero ? "unknown native error" : Marshal.PtrToStringUTF8(pointer);
        return new InvalidOperationException($"AFS native {operation} failed ({code}): {message}");
    }

    [StructLayout(LayoutKind.Sequential)] private struct DecodeStatus { public byte Sb2Ok, Sb3Ok, Sb4Ok; public int Sb2Corrections, Sb3Corrections, Sb4Corrections; }
    private static class Native
    {
        [DllImport(Library, EntryPoint = "lnis_afs_get_abi_version", CallingConvention = CallingConvention.Cdecl)] internal static extern uint GetAbiVersion();
        [DllImport(Library, EntryPoint = "lnis_afs_get_last_error", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr GetLastError();
        [DllImport(Library, EntryPoint = "lnis_afs_encode_frame", CallingConvention = CallingConvention.Cdecl)] internal static extern int Encode(byte toi, byte[] sb2, uint sb2Len, byte[] sb3, uint sb3Len, byte[] sb4, uint sb4Len, byte[] frame, uint frameLen);
        [DllImport(Library, EntryPoint = "lnis_afs_decode_frame", CallingConvention = CallingConvention.Cdecl)] internal static extern int Decode(byte toi, byte[] frame, uint frameLen, byte[] sb2, uint sb2Len, byte[] sb3, uint sb3Len, byte[] sb4, uint sb4Len, out DecodeStatus status);
    }
}
