namespace LnisAfsValidator.Core;

/// <summary>AFS 프레임 복호 후 SB2/SB3/SB4 데이터와 블록별 검증 결과를 전달한다.</summary>
public sealed record AfsDecodedFrame(
    byte[] Sb2Bits, byte[] Sb3Bits, byte[] Sb4Bits,
    bool Sb2Valid, bool Sb3Valid, bool Sb4Valid,
    int Sb2Corrections = 0, int Sb3Corrections = 0, int Sb4Corrections = 0);

/// <summary>6000심볼 AFS 프레임의 인코딩과 복호를 제공하는 코덱 계약이다.</summary>
public interface IAfsFrameCodec : IAsyncDisposable
{
    Task<byte[]> EncodeAsync(int toi, ReadOnlyMemory<byte> sb2Bits, ReadOnlyMemory<byte> sb3Bits, ReadOnlyMemory<byte> sb4Bits, CancellationToken token);
    Task<AfsDecodedFrame> DecodeAsync(int toi, ReadOnlyMemory<byte> frame, CancellationToken token);
}

/// <summary>Test A/E UDP 송수신을 UI와 분리하는 역할별 세션 계약이다.</summary>
public interface IAfsSessionService
{
    Task<AfsSessionResult> SendAsync(AfsSenderSettings settings, AfsTransportSettings transport, IProgress<AfsSessionProgress>? progress, CancellationToken token);
    Task<AfsSessionResult> ReceiveAsync(AfsReceiverSettings settings, AfsTransportSettings transport, IProgress<AfsSessionProgress>? progress, CancellationToken token);
}

/// <summary>GNSS RAW 레코드 한 조각의 순서, 길이와 무결성 정보를 나타낸다.</summary>
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
