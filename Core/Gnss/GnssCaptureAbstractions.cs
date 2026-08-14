namespace LnisAfsValidator.Core;

/// <summary>COM 장치 연결과 GNSS 캡처 파일 생성에 필요한 설정이다.</summary>
public sealed record GnssSerialCaptureSettings(
    string PortName,
    int BaudRate,
    string ProtocolId,
    string OutputRoot,
    string SessionName,
    string ReceiverModel,
    string FirmwareVersion,
    bool DtrEnable = false,
    bool RtsEnable = false);

/// <summary>화면에 표시할 장비 프로토콜 어댑터의 기능 정보다.</summary>
public sealed record GnssProtocolDescriptor(string Id, string DisplayName, string Description, bool ProducesCanonicalRaw);

/// <summary>장비별 직렬 바이트를 공통 GNSS RAW 메시지로 변환하는 확장 지점이다.</summary>
public interface IGnssDeviceProtocolAdapter
{
    GnssProtocolDescriptor Descriptor { get; }
    IReadOnlyList<GnssRawMessage> Push(ReadOnlySpan<byte> bytes);
    void Reset();
}

/// <summary>등록된 장비 프로토콜 목록과 선택된 어댑터 생성을 제공한다.</summary>
public interface IGnssProtocolAdapterCatalog
{
    IReadOnlyList<GnssProtocolDescriptor> Protocols { get; }
    IGnssDeviceProtocolAdapter Create(string protocolId);
}

/// <summary>SerialPort와 시험용 메모리 입력을 같은 캡처 서비스에서 읽기 위한 바이트 소스다.</summary>
public interface IGnssByteSource : IAsyncDisposable
{
    string Description { get; }
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}

/// <summary>설정에 맞는 GNSS 바이트 소스를 연다.</summary>
public interface IGnssByteSourceFactory
{
    ValueTask<IGnssByteSource> OpenAsync(GnssSerialCaptureSettings settings, CancellationToken cancellationToken);
}

/// <summary>운영체제에서 사용할 수 있는 직렬 포트 이름을 조회한다.</summary>
public interface IGnssSerialPortCatalog
{
    IReadOnlyList<string> GetPortNames();
}

/// <summary>COM 원본 보존과 선택 프로토콜의 capture.graw 생성을 수행한다.</summary>
public interface IGnssCaptureService
{
    Task<GnssCaptureResult> CaptureAsync(
        GnssSerialCaptureSettings settings,
        IProgress<GnssCaptureProgress>? progress,
        CancellationToken cancellationToken);
}
