namespace LnisAfsValidator.Core;

/// <summary>AFS UDP 세션에서 교환하는 제어 및 데이터 패킷 종류다.</summary>
public enum AfsPacketKind : byte { TimeSyncRequest = 1, TimeSyncResponse = 2, SessionStart = 3, Frame = 4, Probe = 5, ProbeResponse = 6, SessionEnd = 7, Result = 8 }

/// <summary>UDP wire format으로 직렬화되는 논리 AFS 패킷이다.</summary>
public sealed record AfsPacket(
    AfsPacketKind Kind,
    Guid TestId,
    uint Sequence,
    byte CopyIndex,
    byte Prn,
    ushort Week,
    ushort IntervalOfWeek,
    byte TimeOfInterval,
    long SentUtcTicks,
    byte[] Payload);

/// <summary>송수신 완료 후 성능 지표 계산에 사용하는 누적 네트워크 계수다.</summary>
public sealed record AfsNetworkCounters(
    long ExpectedLogicalFrames,
    long ReceivedLogicalFrames,
    long SentDatagrams,
    long ReceivedDatagrams,
    long DuplicateDatagrams,
    long CorruptDatagrams,
    long ProbeAttempts,
    long ProbeResponses,
    long RawBytes,
    TimeSpan TransferDuration,
    IReadOnlyList<double> OneWayLatencyMilliseconds,
    double? AverageLatencyMilliseconds = null,
    double? MaximumLatencyMilliseconds = null,
    long SimulatedDroppedDatagrams = 0,
    double ConfiguredDropRatePercent = 0);

/// <summary>수신 프로세스의 특정 시점 CPU와 작업 집합 메모리 표본이다.</summary>
public sealed record ResourceSample(DateTimeOffset Timestamp, double CpuPercent, long WorkingSetBytes);
