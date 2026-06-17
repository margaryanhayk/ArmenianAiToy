using System.Diagnostics;

namespace ArmenianAiToy.Tools.ContentPackBuilder;

/// <summary>
/// Production <see cref="IAudioReencoder"/> — shells out to <c>ffmpeg</c>
/// to re-encode an MP3 to mono at the target bitrate (e.g. 48 kbps speech).
/// ffmpeg must be on PATH. Not unit-tested (external process); the tests
/// drive <see cref="ContentPackBuilder"/> through a stub reencoder.
/// </summary>
public sealed class FfmpegReencoder : IAudioReencoder
{
    private readonly string _ffmpegPath;

    public FfmpegReencoder(string ffmpegPath = "ffmpeg") => _ffmpegPath = ffmpegPath;

    public async Task<byte[]> ReencodeAsync(byte[] mp3, int kbps, CancellationToken ct)
    {
        var inPath = Path.Combine(Path.GetTempPath(), $"areg-cpb-{Guid.NewGuid():N}-in.mp3");
        var outPath = Path.Combine(Path.GetTempPath(), $"areg-cpb-{Guid.NewGuid():N}-out.mp3");
        try
        {
            await File.WriteAllBytesAsync(inPath, mp3, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // -y overwrite, -ac 1 mono, -b:a <k> CBR, force mp3 muxer.
            foreach (var arg in new[]
                     { "-y", "-loglevel", "error", "-i", inPath,
                       "-ac", "1", "-b:a", $"{kbps}k", "-f", "mp3", outPath })
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffmpeg exited {proc.ExitCode}: {stderr.Trim()}");
            }
            return await File.ReadAllBytesAsync(outPath, ct);
        }
        finally
        {
            TryDelete(inPath);
            TryDelete(outPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
