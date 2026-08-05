namespace LnisAfsValidator.Core;

public enum GnssRawMessageType : byte { ObservationEpoch = 1, NavigationUpdate = 2, ReceiverMetadata = 3 }
public enum GnssConstellation : byte { Gps = 0, Galileo = 2 }

public sealed record GnssRawEnvelope(
    ushort SchemaVersion,
    Guid TestId,
    Guid MessageId,
    ulong SequenceNumber,
    DateTimeOffset CapturedAtUtc,
    GnssRawMessage Message);

public abstract record GnssRawMessage(GnssRawMessageType Type);

public sealed record ObservationEpochMessage(
    double ReceiverTowSeconds,
    ushort Week,
    sbyte LeapSeconds,
    byte ReceiverStatus,
    byte RawxVersion,
    IReadOnlyList<GnssObservation> Observations)
    : GnssRawMessage(GnssRawMessageType.ObservationEpoch);

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

public sealed record NavigationUpdateMessage(
    GnssConstellation Constellation,
    byte SatelliteId,
    byte SignalId,
    byte FrequencyId,
    byte SfrbxVersion,
    IReadOnlyList<uint> Words)
    : GnssRawMessage(GnssRawMessageType.NavigationUpdate);

public sealed record ReceiverMetadataMessage(
    string ReceiverModel,
    string FirmwareVersion,
    string PortName,
    int BaudRate,
    string SessionName)
    : GnssRawMessage(GnssRawMessageType.ReceiverMetadata);

public sealed record GnssCaptureStatistics(
    long BytesRead,
    long ValidFrames,
    long RawxFrames,
    long SfrbxFrames,
    long ChecksumErrors,
    long UnsupportedFrames,
    long UnsupportedConstellations,
    long EnvelopesWritten);

public sealed record GnssCaptureProgress(GnssCaptureStatistics Statistics, string Message);
public sealed record GnssCaptureResult(string Directory, string RawUbxPath, string CanonicalPath, string ManifestPath, GnssCaptureStatistics Statistics, bool Completed, string? Error);

