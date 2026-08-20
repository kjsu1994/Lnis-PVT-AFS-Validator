using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>AFS 송수신 결과 디렉터리, 복원 RAW, JSON과 CSV 파일 저장을 담당한다.</summary>
public sealed class AfsResultWriter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string CreateDirectory(string resultRoot, Guid testId, string role)
    {
        var directory = Path.Combine(
            resultRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{testId:N}-afs-{role}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public async Task<string> WriteReconstructedAsync(
        string directory,
        IReadOnlyList<(uint Sequence, byte[] Record)> records,
        CancellationToken token)
    {
        var path = Path.Combine(directory, "reconstructed.graw");
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            true);
        foreach (var pair in records)
        {
            var size = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)pair.Record.Length));
            await stream.WriteAsync(size, token);
            await stream.WriteAsync(pair.Record, token);
        }
        return path;
    }

    public async Task WriteResultAsync(
        AfsSessionResult result,
        IReadOnlyList<ResourceSample> samples,
        CancellationToken token)
    {
        await File.WriteAllTextAsync(
            Path.Combine(result.ResultDirectory, "result.json"),
            JsonSerializer.Serialize(result, Json),
            token);
        await File.WriteAllLinesAsync(
            Path.Combine(result.ResultDirectory, "metrics-summary.csv"),
            new[] { "Category,Name,Description,Value,Unit,Status" }
                .Concat(result.Metrics.Select(metric =>
                    $"""{metric.Category},{metric.Name},"{metric.Description}",{metric.Value},{metric.Unit},{metric.Status}""")),
            Encoding.UTF8,
            token);
        await File.WriteAllLinesAsync(
            Path.Combine(result.ResultDirectory, "metrics-timeseries.csv"),
            new[] { "Timestamp,CpuPercent,WorkingSetBytes" }
                .Concat(samples.Select(sample =>
                    $"{sample.Timestamp:O},{sample.CpuPercent},{sample.WorkingSetBytes}")),
            Encoding.UTF8,
            token);
    }
}
