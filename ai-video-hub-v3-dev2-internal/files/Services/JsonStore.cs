using System.Collections.Concurrent;
using System.Text.Json;

namespace AI.VideoHub.V3.Services;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim Gate(string path) => FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    public static async Task<T> LoadAsync<T>(string path, T fallback)
    {
        var gate = Gate(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return fallback;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options).ConfigureAwait(false) ?? fallback;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"JSON load failed: {path}: {ex.Message}");
            return fallback;
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task SaveAsync<T>(string path, T value)
    {
        var gate = Gate(path);
        await gate.WaitAsync().ConfigureAwait(false);
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            File.Move(temp, path, true);
            temp = null;
        }
        finally
        {
            if (temp is not null)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
            gate.Release();
        }
    }
}
