using System.Diagnostics;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

public static class AfsPerformanceCalculator
{
    public static IReadOnlyList<PerformanceMetric> Calculate(AfsNetworkCounters c, IReadOnlyDictionary<string, MetricThreshold>? thresholds = null)
    {
        thresholds ??= new Dictionary<string, MetricThreshold>(); var metrics = new List<PerformanceMetric>();
        var availability = Ratio(c.ProbeResponses, c.ProbeAttempts);
        double? loss = c.ExpectedLogicalFrames == 0 ? null : 100.0 * (c.ExpectedLogicalFrames - c.ReceivedLogicalFrames) / c.ExpectedLogicalFrames;
        var delivery = Ratio(c.ReceivedLogicalFrames, c.ExpectedLogicalFrames);
        var seconds = c.TransferDuration.TotalSeconds; double? goodput = seconds > 0 ? c.RawBytes / seconds : null;
        Add(PerformanceCategory.Network, "LinkAvailability", "링크가 사용 가능한 시간 비율", "%", availability, true);
        Add(PerformanceCategory.Network, "AverageLatency", "송신부터 수신까지 걸리는 평균 시간", "ms", c.AverageLatencyMilliseconds ?? (c.OneWayLatencyMilliseconds.Count == 0 ? null : c.OneWayLatencyMilliseconds.Average()), false);
        Add(PerformanceCategory.Network, "MaximumLatency", "시험 중 발생한 최대 전달 지연", "ms", c.MaximumLatencyMilliseconds ?? (c.OneWayLatencyMilliseconds.Count == 0 ? null : c.OneWayLatencyMilliseconds.Max()), false);
        Add(PerformanceCategory.Network, "Throughput", "단위 시간당 전달된 데이터량", "byte/s", goodput, true);
        var attemptedDatagrams = c.SentDatagrams + c.SimulatedDroppedDatagrams;
        var injectedDropRate = attemptedDatagrams == 0 ? 0 : 100.0 * c.SimulatedDroppedDatagrams / attemptedDatagrams;
        metrics.Add(new(PerformanceCategory.Network, "InjectedUdpDropRate", "Test E에서 송신 직전에 의도적으로 제거한 Frame 데이터그램 비율", "%", injectedDropRate, MetricStatus.Measured,
            Detail: $"설정값 {c.ConfiguredDropRatePercent:0.###}%, 제거 {c.SimulatedDroppedDatagrams}/{attemptedDatagrams}"));
        Add(PerformanceCategory.Routing, "PacketLossRate", "수신되지 않은 데이터 비율", "%", loss, false);
        Add(PerformanceCategory.Routing, "PacketDeliveryRate", "최종 수신 성공 비율", "%", delivery, true);
        NotApplicable(PerformanceCategory.Routing, "ReroutingTime", "새로운 경로가 적용되는 시간", "ms", "HDTN/대체 경로가 구성되지 않았습니다.");
        NotApplicable(PerformanceCategory.Routing, "RoutingOverhead", "전체 전송량 중 제어 메시지가 차지하는 비율", "%", "HDTN 라우팅 제어 메시지가 없습니다.");
        NotApplicable(PerformanceCategory.Routing, "PathStability", "일정 시간 유지되는 경로의 지속성", "s", "관찰할 라우팅 경로가 없습니다.");
        NotApplicable(PerformanceCategory.Pvt, "PositionError", "기준 위치 산출 값과 PVT 산출 값의 차이", "m", "PVT Solver가 구현되지 않았습니다.");
        NotApplicable(PerformanceCategory.Pvt, "TimeError", "기준 시간 대비 산출 시간 차이", "s", "PVT Solver가 구현되지 않았습니다.");
        NotApplicable(PerformanceCategory.Pvt, "PvtDeliveryLatency", "PVT 정보가 수신되는 데 걸리는 시간", "ms", "PVT Solver가 구현되지 않았습니다.");
        return metrics;

        double? Ratio(long numerator, long denominator) => denominator <= 0 ? null : 100.0 * numerator / denominator;
        void Add(PerformanceCategory category, string name, string description, string unit, double? value, bool minimum)
        {
            thresholds.TryGetValue(name, out var threshold); MetricStatus status;
            if (value is null) status = MetricStatus.NotApplicable;
            else if (threshold is not { Enabled: true }) status = MetricStatus.Measured;
            else status = (threshold.IsMinimum ? value >= threshold.Value : value <= threshold.Value) ? MetricStatus.Pass : MetricStatus.Fail;
            metrics.Add(new(category, name, description, unit, value, status, threshold));
        }
        void NotApplicable(PerformanceCategory category, string name, string description, string unit, string detail) => metrics.Add(new(category, name, description, unit, null, MetricStatus.NotApplicable, null, detail));
    }
}

public sealed class ProcessResourceSampler : IAsyncDisposable
{
    private readonly List<ResourceSample> samples = [];
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task loop;
    public ProcessResourceSampler(TimeSpan? interval = null) => loop = SampleAsync(interval ?? TimeSpan.FromMilliseconds(250));
    public IReadOnlyList<ResourceSample> Samples { get { lock (samples) return samples.ToArray(); } }

    private async Task SampleAsync(TimeSpan interval)
    {
        using var process = Process.GetCurrentProcess(); var lastTime = Stopwatch.GetTimestamp(); var lastCpu = process.TotalProcessorTime;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellation.Token); process.Refresh(); var now = Stopwatch.GetTimestamp(); var cpu = process.TotalProcessorTime;
                var elapsed = Stopwatch.GetElapsedTime(lastTime, now).TotalSeconds; var used = (cpu - lastCpu).TotalSeconds;
                var percent = elapsed <= 0 ? 0 : 100.0 * used / elapsed / Environment.ProcessorCount;
                lock (samples) samples.Add(new(DateTimeOffset.UtcNow, percent, process.WorkingSet64)); lastTime = now; lastCpu = cpu;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync() { cancellation.Cancel(); try { await loop; } finally { cancellation.Dispose(); } }
}
