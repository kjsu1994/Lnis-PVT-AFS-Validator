namespace LnisAfsValidator.Core;

public enum AfsPacketKind : byte { TimeSyncRequest = 1, TimeSyncResponse = 2, SessionStart = 3, Frame = 4, Probe = 5, ProbeResponse = 6, SessionEnd = 7, Result = 8 }

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

public sealed record ResourceSample(DateTimeOffset Timestamp, double CpuPercent, long WorkingSetBytes);
