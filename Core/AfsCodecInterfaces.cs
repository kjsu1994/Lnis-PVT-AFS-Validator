namespace LnisAfsValidator.Core;

public sealed record AfsDecodedFrame(byte[] Sb2Bits, byte[] Sb3Bits, byte[] Sb4Bits, bool Sb2Valid, bool Sb3Valid, bool Sb4Valid);

public interface IAfsFrameCodec : IAsyncDisposable
{
    Task<byte[]> EncodeAsync(int toi, ReadOnlyMemory<byte> sb2Bits, ReadOnlyMemory<byte> sb3Bits, ReadOnlyMemory<byte> sb4Bits, CancellationToken token);
    Task<AfsDecodedFrame> DecodeAsync(int toi, ReadOnlyMemory<byte> frame, CancellationToken token);
}

public sealed record AfsRawFragment(
    byte Version,
    byte Flags,
    uint RecordSequence,
    ushort FragmentIndex,
    ushort FragmentCount,
    uint RecordLength,
    byte PayloadLength,
    uint RecordCrc32,
    byte[] Payload);

