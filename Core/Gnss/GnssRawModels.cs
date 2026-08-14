namespace LnisAfsValidator.Core;

// GNSS 캡처 파일에 저장되는 메시지 유형과 정규화된 데이터 구조를 정의한다.
/// <summary>정규화 RAW 파일에 저장할 메시지 종류다.</summary>
public enum GnssRawMessageType : byte { ObservationEpoch = 1, NavigationUpdate = 2, ReceiverMetadata = 3 }
/// <summary>관측값이 속한 GNSS 위성군이다.</summary>
public enum GnssConstellation : byte { Gps = 0, Galileo = 2 }

/// <summary>메시지 본문과 시험·수신기·캡처 시각 식별 정보를 함께 보관한다.</summary>
public sealed record GnssRawEnvelope(
    ushort SchemaVersion,
    Guid TestId,
    Guid MessageId,
    ulong SequenceNumber,
    DateTimeOffset CapturedAtUtc,
    GnssRawMessage Message);

/// <summary>정규화된 GNSS RAW 메시지의 공통 기반 형식이다.</summary>
public abstract record GnssRawMessage(GnssRawMessageType Type);

/// <summary>한 관측 시각의 GNSS 주차, 수신기 시각과 위성별 측정값을 나타낸다.</summary>
public sealed record ObservationEpochMessage(
    double ReceiverTowSeconds,
    ushort Week,
    sbyte LeapSeconds,
    byte ReceiverStatus,
    byte RawxVersion,
    IReadOnlyList<GnssObservation> Observations)
    : GnssRawMessage(GnssRawMessageType.ObservationEpoch);

/// <summary>단일 위성 신호에서 수집한 의사거리, 반송파, 도플러와 품질 값이다.</summary>
public sealed record GnssObservation(
    GnssConstellation Constellation,
    byte SatelliteId,
    byte SignalId,
    byte FrequencyId,
    double PseudorangeMeters,
    double CarrierPhaseCycles,
    float DopplerHz,
    ushort LockTimeMilliseconds,
    byte CarrierToNoiseDbHz,
    byte PseudorangeStdDev,
    byte CarrierPhaseStdDev,
    byte DopplerStdDev,
    byte TrackingStatus);

/// <summary>위성 항법 데이터 워드 묶음을 나타낸다.</summary>
public sealed record NavigationUpdateMessage(
    GnssConstellation Constellation,
    byte SatelliteId,
    byte SignalId,
    byte FrequencyId,
    byte SfrbxVersion,
    IReadOnlyList<uint> Words)
    : GnssRawMessage(GnssRawMessageType.NavigationUpdate);

/// <summary>RAW 파일을 생성한 수신기와 세션 정보를 나타낸다.</summary>
public sealed record ReceiverMetadataMessage(
    string ReceiverModel,
    string FirmwareVersion,
    string PortName,
    int BaudRate,
    string SessionName)
    : GnssRawMessage(GnssRawMessageType.ReceiverMetadata);

/// <summary>GNSS RAW 입력 처리 중 누적한 프레임·오류 통계다.</summary>
public sealed record GnssCaptureStatistics(
    long BytesRead,
    long ValidFrames,
    long RawxFrames,
    long SfrbxFrames,
    long ChecksumErrors,
    long UnsupportedFrames,
    long UnsupportedConstellations,
    long EnvelopesWritten);

/// <summary>GNSS RAW 처리 진행률 메시지와 현재 통계다.</summary>
public sealed record GnssCaptureProgress(GnssCaptureStatistics Statistics, string Message);
/// <summary>GNSS RAW 처리 산출물 경로와 최종 상태다.</summary>
public sealed record GnssCaptureResult(string Directory, string RawUbxPath, string CanonicalPath, string ManifestPath, GnssCaptureStatistics Statistics, bool Completed, string? Error);

