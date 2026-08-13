namespace LnisAfsValidator.Core;

/// <summary>표준 GNSS RAW envelope와 바이너리 레코드 사이의 변환 계약이다.</summary>
public interface IGnssRawCodec
{
    byte[] Encode(GnssRawEnvelope envelope);
    GnssRawEnvelope Decode(ReadOnlySpan<byte> data);
}

/// <summary>파일 또는 시리얼 포트에서 GNSS RAW 메시지를 비동기로 공급하는 계약이다.</summary>
public interface IGnssRawSource
{
    IAsyncEnumerable<GnssRawEnvelope> ReadAsync(CancellationToken token);
}

/// <summary>GNSS 입력을 캡처하여 원본, 정규화 파일과 통계를 생성하는 계약이다.</summary>
public interface IGnssCaptureService
{
    Task<GnssCaptureResult> CaptureSerialAsync(string portName, int baudRate, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token);
    Task<GnssCaptureResult> ReplayFileAsync(string sourcePath, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token);
}

