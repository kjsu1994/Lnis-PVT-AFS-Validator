using System.Text.Json;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
/// <summary>시험별 결과 디렉터리를 만들고 설정, 로그와 최종 결과를 파일로 저장한다.</summary>
public sealed class RunStore(string root) : IRunStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string CreateRunDirectory(Guid id) { var p = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{id:N}"); Directory.CreateDirectory(p); return p; }
    public async Task SaveJsonAsync<T>(string d, string n, T v, CancellationToken t) { await using var f = File.Create(Path.Combine(d, n)); await JsonSerializer.SerializeAsync(f, v, Json, t); }
    public Task SaveTextAsync(string d, string n, IEnumerable<ProcessLogLine> l, CancellationToken t) => File.WriteAllLinesAsync(Path.Combine(d, n), l.Select(x => $"{x.Timestamp:O} [{(x.IsError ? "ERR" : "OUT")}] {x.Text}"), t);
    public Task ApplyRetentionAsync(Verdict v, IEnumerable<string> files, CancellationToken t) { if (v == Verdict.Pass) foreach (var f in files.Where(File.Exists)) { t.ThrowIfCancellationRequested(); File.Delete(f); } return Task.CompletedTask; }
}
