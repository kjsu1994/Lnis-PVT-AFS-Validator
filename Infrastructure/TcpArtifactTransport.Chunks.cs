using System.Buffers.Binary;
using System.Net.Sockets;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
public sealed partial class TcpArtifactTransport
{
    private static async Task SendChunksAsync(NetworkStream stream, string path, TransferManifest m, IProgress<RunProgress>? progress, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, m.ChunkSize, true);
        if (file.Length != m.FileLength) throw new InvalidDataException("File length changed after manifest creation.");
        var buffer = new byte[m.ChunkSize]; var header = new byte[12]; long total = 0; var index = 0;
        int read;
        while ((read = await file.ReadAsync(buffer, token)) > 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(header, index); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), read); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), Hashing.Crc32(buffer.AsSpan(0, read)));
            await stream.WriteAsync(header, token); await stream.WriteAsync(buffer.AsMemory(0, read), token); total += read; index++;
            progress?.Report(new(RunState.Transferring, 30 + 40.0 * total / m.FileLength, $"Sent {total:N0}/{m.FileLength:N0} bytes"));
        }
    }

    private static async Task<TransferReceipt> ReceiveChunksAsync(NetworkStream stream, string partial, TransferManifest m, TimeSpan idle, IProgress<RunProgress>? progress, CancellationToken token)
    {
        var header = new byte[12]; var buffer = new byte[m.ChunkSize]; long total = 0; var expected = 0; string? error = null;
        await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, m.ChunkSize, true))
        {
            while (total < m.FileLength)
            {
                await ReadExactlyAsync(stream, header, idle, token); var index = BinaryPrimitives.ReadInt32BigEndian(header); var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)); var crc = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
                if (index != expected) error ??= $"Expected chunk {expected}, received {index}";
                if (length <= 0 || length > m.ChunkSize || total + length > m.FileLength) throw new InvalidDataException("Invalid chunk length; frame boundary cannot be trusted.");
                await ReadExactlyAsync(stream, buffer.AsMemory(0, length), idle, token); if (Hashing.Crc32(buffer.AsSpan(0, length)) != crc) error ??= $"CRC32 mismatch at chunk {index}";
                await file.WriteAsync(buffer.AsMemory(0, length), token); total += length; expected++;
                progress?.Report(new(RunState.Transferring, 30 + 40.0 * total / m.FileLength, $"Received {total:N0}/{m.FileLength:N0} bytes"));
            }
        }
        var hash = await Hashing.Sha256Async(partial, token); if (!hash.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) error ??= "SHA-256 mismatch";
        if (error is not null) return new(false, error, partial, total, expected, hash);
        var complete = Path.ChangeExtension(partial, ".iq"); File.Move(partial, complete);
        return new(true, "CRC32, byte count and SHA-256 verified", complete, total, expected, hash);
    }
}
