using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Spek.Net;

public delegate void SampleHandler(ReadOnlySpan<float> samples);

public sealed class FfmpegException(string message, string? stderr = null) : Exception(message)
{
    public string? Stderr { get; } = stderr;
}

// Runs ffmpeg.exe to read stream information and to decode one channel to 32-bit float samples.
public sealed class Ffmpeg(string executablePath)
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<string, string>>>> DecoderCache =
        new(StringComparer.OrdinalIgnoreCase);

    public string ExecutablePath { get; } = executablePath;

    // Returns the first candidate that exists, or else ffmpeg from PATH.
    public static string? Find(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(full)) return full;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    public async Task<AudioFileInfo> ProbeAsync(string file, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ExecutablePath)) return AudioFileInfo.Failed(file, AudioError.FfmpegNotFound, ExecutablePath);

        var decodersTask = GetDecodersAsync();
        var (_, _, stderr) = await RunAsync(["-hide_banner", "-nostdin", "-i", file], cancellationToken).ConfigureAwait(false);

        Dictionary<string, string>? decoders = null;
        try
        {
            decoders = await decodersTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FfmpegException or Win32Exception or InvalidOperationException)
        {
            // The descriptions are cosmetic. Without them the stream shows ffmpeg's short codec name.
        }

        return FfmpegOutputParser.ParseInput(file, stderr, name => decoders?.GetValueOrDefault(name));
    }

    public async Task DecodeAsync(string file, int stream, int channel, SampleHandler onSamples, CancellationToken cancellationToken = default)
    {
        string[] args =
        [
            "-hide_banner", "-nostdin", "-v", "error",
            "-i", file,
            "-map", $"0:a:{stream}",
            "-af", $"pan=mono|c0=c{channel}",
            "-f", "f32le", "-acodec", "pcm_f32le", "-",
        ];

        using var process = Start(args);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await using var registration = cancellationToken.Register(() => Kill(process));

        var output = process.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024];
        int carry = 0;
        try
        {
            while (true)
            {
                int read = await output.ReadAsync(buffer.AsMemory(carry), CancellationToken.None).ConfigureAwait(false);
                if (read == 0) break;
                cancellationToken.ThrowIfCancellationRequested();

                int total = carry + read;
                int usable = total & ~3;
                onSamples(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, usable)));
                carry = total - usable;
                if (carry > 0) Buffer.BlockCopy(buffer, usable, buffer, 0, carry);
            }
        }
        catch
        {
            Kill(process);
            throw;
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var lastLine = stderr.Replace("\r", "").Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim();
            throw new FfmpegException(lastLine ?? $"ffmpeg exited with code {process.ExitCode}.", stderr);
        }
    }

    private Task<Dictionary<string, string>> GetDecodersAsync() =>
        DecoderCache.GetOrAdd(ExecutablePath, _ => new Lazy<Task<Dictionary<string, string>>>(LoadDecodersAsync)).Value;

    private async Task<Dictionary<string, string>> LoadDecodersAsync()
    {
        try
        {
            var (_, stdout, _) = await RunAsync(["-hide_banner", "-decoders"], CancellationToken.None).ConfigureAwait(false);
            return FfmpegOutputParser.ParseDecoders(stdout);
        }
        catch
        {
            DecoderCache.TryRemove(ExecutablePath, out _);
            throw;
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        using var process = Start(args);
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private Process Start(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        return Process.Start(info) ?? throw new FfmpegException("ffmpeg didn't start.");
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
        }
    }
}
