namespace LnisAfsValidator.Core;

public interface IGnssRawCodec
{
    byte[] Encode(GnssRawEnvelope envelope);
    GnssRawEnvelope Decode(ReadOnlySpan<byte> data);
}

public interface IGnssRawSource
{
    IAsyncEnumerable<GnssRawEnvelope> ReadAsync(CancellationToken token);
}

public interface IGnssCaptureService
{
    Task<GnssCaptureResult> CaptureSerialAsync(string portName, int baudRate, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token);
    Task<GnssCaptureResult> ReplayFileAsync(string sourcePath, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token);
}

