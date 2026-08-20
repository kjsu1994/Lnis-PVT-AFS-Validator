using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>AFS 송수신 설정을 검증하고 역할별 세션 Handler에 실행을 위임한다.</summary>
public sealed class AfsSessionOrchestrator : IAfsSessionService
{
    private readonly AfsSendSessionHandler sender;
    private readonly AfsReceiveSessionHandler receiver;

    public AfsSessionOrchestrator(
        AfsFrameService frameService,
        AfsTimeSynchronizer timeSynchronizer,
        AfsTestEvaluator evaluator,
        AfsResultWriter resultWriter)
    {
        sender = new(frameService, timeSynchronizer, evaluator, resultWriter);
        receiver = new(frameService, timeSynchronizer, evaluator, resultWriter);
    }

    public Task<AfsSessionResult> SendAsync(
        AfsSenderSettings settings,
        AfsTransportSettings transport,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        Validate(settings, transport);
        return sender.SendAsync(settings, transport, progress, token);
    }

    public Task<AfsSessionResult> ReceiveAsync(
        AfsReceiverSettings settings,
        AfsTransportSettings transport,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        Validate(settings, transport);
        return receiver.ReceiveAsync(settings, transport, progress, token);
    }

    private static void Validate(
        AfsSenderSettings settings,
        AfsTransportSettings transport)
    {
        if (!File.Exists(settings.CapturePath))
            throw new FileNotFoundException(
                "capture.graw 파일을 찾을 수 없습니다.",
                settings.CapturePath);
        if ((settings.TestType is AfsEndToEndTestType.TestB_RandomErrors or
             AfsEndToEndTestType.TestC_BurstErrors) &&
            settings.ErrorCount is < 1 or > AfsProtocolLimits.SubframePayloadSymbolCount)
            throw new ArgumentException("Test B/C 오류 개수는 1~5880 범위여야 합니다.");
        if (settings.TestType == AfsEndToEndTestType.TestD_SyncRecovery &&
            settings.ErrorCount is < 1 or > AfsProtocolLimits.SyncPatternSymbolCount)
            throw new ArgumentException("Test D SP 오류 개수는 1~68 범위여야 합니다.");
        if (settings.SyncDamageInterval < 1)
            throw new ArgumentException("Test D 손상 간격은 1 이상이어야 합니다.");
        ValidateCommon(
            settings.ResultRoot,
            settings.Prn,
            settings.CustomMessageType,
            transport);
    }

    private static void Validate(
        AfsReceiverSettings settings,
        AfsTransportSettings transport) =>
        ValidateCommon(
            settings.ResultRoot,
            settings.Prn,
            settings.CustomMessageType,
            transport);

    private static void ValidateCommon(
        string resultRoot,
        int prn,
        int customMessageType,
        AfsTransportSettings transport)
    {
        if (prn != 8 || customMessageType != 63)
            throw new ArgumentException("AFS v1은 PRN 8과 Custom Type 63만 지원합니다.");
        if (transport.DataPort is < 1 or > 65535 ||
            transport.ResultPort is < 1 or > 65535 ||
            transport.DataPort == transport.ResultPort)
            throw new ArgumentException(
                "데이터 포트와 결과 포트는 서로 다른 1~65535 값이어야 합니다.");
        if (transport.RepeatCount is < 1 or > 20)
            throw new ArgumentException("중복 송신 횟수는 1~20 범위여야 합니다.");
        if (transport.SimulatedDropRatePercent is < 0 or > 100)
            throw new ArgumentException("의도적 UDP Drop Rate는 0~100% 범위여야 합니다.");
        Directory.CreateDirectory(resultRoot);
    }
}
