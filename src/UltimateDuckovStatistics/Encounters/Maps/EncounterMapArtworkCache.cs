using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UltimateDuckovStatistics.Encounters;

// One bounded GPU/readback/encoder pipeline. Native assets are borrowed, never destroyed.
internal sealed class EncounterMapArtworkCache : IDisposable
{
    private const int MaximumDimension = 2048;
    private const int MaximumPngBytes = 20 * 1024 * 1024;
    private readonly IEncounterObservationSink sink;
    private readonly Action<string> log;
    private readonly string directory;
    private readonly HashSet<string> attempted = new(StringComparer.Ordinal);
    private Task<CacheResult>? worker;
    private volatile bool disposed;
    private string? pendingFailure;
    private bool readbackPending;

    public EncounterMapArtworkCache(IEncounterObservationSink sink, string outputDirectory, Action<string> log)
    {
        this.sink = sink;
        this.log = log;
        directory = Path.Combine(Path.GetFullPath(outputDirectory), "maps-cache");
        // Passing the same session directory after process restart validates a persisted image
        // without loading the source gameplay scene. File IO stays off the gameplay thread.
        worker = Task.Run(ReadExisting);
    }

    public string Status { get; private set; } = "cache validation pending";

    public void Tick()
    {
        if (disposed) return;
        if (pendingFailure != null)
        {
            var failure = pendingFailure;
            pendingFailure = null;
            Record("map-cache-unavailable", new { reason = failure });
        }
        if (worker == null || !worker.IsCompleted) return;
        var completed = worker;
        worker = null;
        try
        {
            var result = completed.GetAwaiter().GetResult();
            if (result.Png == null)
            {
                Status = result.Error ?? "cache idle";
                if (result.Error != null) Record("map-cache-unavailable", new { result.Key, reason = result.Error });
                return;
            }
            // The detached bytes and SHA are verified on a worker; Unity decode is main-thread only.
            Texture2D? owned = null;
            try
            {
                owned = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var loaded = ImageConversion.LoadImage(owned, result.Png, markNonReadable: true);
                var valid = loaded && owned.width == result.Width && owned.height == result.Height;
                if (valid) attempted.Add(result.Key);
                Status = valid ? "durable cache decoded successfully" : "cache decode failed";
                Record("map-cache-validation", new { result.Key, result.Existing, valid, result.Width, result.Height, bytes = result.Png.Length, result.PngHash, path = result.Path, test = "hash-and-owned-Unity-decode", visualOrientationVerified = false });
            }
            finally { if (owned != null) Object.Destroy(owned); }
        }
        catch (Exception exception) { ReportFailure("cache completion", exception); }
    }

    public void TryCapture(string key, string calibrationJson, Sprite sprite)
    {
        if (disposed || readbackPending || worker != null || attempted.Contains(key)) return;
        if (attempted.Count >= 64) return;
        attempted.Add(key);
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Status = "async GPU readback unsupported";
            Record("map-cache-unavailable", new { calibrationId = key, reason = Status });
            return;
        }

        GpuLease? lease = null;
        try
        {
            var rect = sprite.rect;
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension)
                throw new InvalidOperationException("Sprite full canvas exceeds 2048px bound.");
            // Installed map sprites use an unatlased full source canvas (verified in map-sprites.json).
            // Copy that exact canvas, including transparent margins. A trimmed rendering mesh does
            // not change the canvas origin. Never crop/rotate a packed sprite by guessing its UVs.
            if (sprite.packed || rect.x != 0 || rect.y != 0 || rect.width != sprite.texture.width || rect.height != sprite.texture.height
                || sprite.associatedAlphaSplitTexture != null)
                throw new InvalidOperationException("Packed/cropped/alpha-split sprite needs a separately qualified renderer; full-canvas cache unavailable.");
            lease = new GpuLease();
            lease.BorrowedSprite = sprite;
            lease.Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "UDS encounter map cache target", useMipMap = false, autoGenerateMips = false, antiAliasing = 1
            };
            if (!lease.Target.Create()) throw new InvalidOperationException("Offscreen render target creation failed.");
            var previousTarget = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            try { GL.sRGBWrite = false; Graphics.Blit(sprite.texture, lease.Target); }
            finally { GL.sRGBWrite = previousSrgbWrite; RenderTexture.active = previousTarget; }
            var ownedLease = lease;
            var linearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            readbackPending = true;
            Status = "async GPU readback pending";
            AsyncGPUReadback.Request(lease.Target, 0, TextureFormat.RGBA32, request =>
            {
                // Unity invokes this callback on its update thread. The lease is intentionally
                // retained through Dispose until this request completes; it is not destroyed early.
                try
                {
                    if (disposed) return;
                    if (request.hasError) throw new InvalidOperationException("Async GPU readback failed.");
                    var data = request.GetData<byte>();
                    if (data.Length != width * height * 4) throw new InvalidOperationException("Unexpected RGBA readback length.");
                    var detached = data.ToArray();
                    worker = Task.Run(() => EncodeAndCommit(key, calibrationJson, detached, width, height, linearColorSpace));
                    Status = "PNG encode and atomic persistence pending";
                }
                catch (Exception exception) { ReportFailure("GPU readback", exception); }
                finally
                {
                    try { ownedLease.Dispose(); }
                    catch (Exception exception) { ReportFailure("GPU lease cleanup", exception); }
                    readbackPending = false;
                }
            });
            lease = null;
        }
        catch (Exception exception) { readbackPending = false; ReportFailure("artwork capture", exception); }
        finally { lease?.Dispose(); }
    }

    private CacheResult EncodeAndCommit(string key, string calibrationJson, byte[] rgba, int width, int height, bool linearColorSpace)
    {
        try
        {
            if (disposed) return new CacheResult();
            // The offscreen blit keeps straight alpha. A linear project samples sRGB source
            // textures into our linear target; encode those RGB values back to sRGB for PNG.
            if (linearColorSpace) for (var i = 0; i < rgba.Length; i += 4)
            {
                for (var component = 0; component < 3; component++)
                {
                    var straight = rgba[i + component] / 255.0;
                    straight = straight <= 0.0031308 ? 12.92 * straight : 1.055 * Math.Pow(straight, 1.0 / 2.4) - 0.055;
                    rgba[i + component] = (byte)Math.Min(255, Math.Max(0, Math.Round(straight * 255)));
                }
            }
            // Native installed binding + Unity primary documentation declare EncodeArrayToPNG
            // thread safe. This worker never uses a Texture/Sprite/RenderTexture instance.
            var png = ImageConversion.EncodeArrayToPNG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
            if (png.Length == 0 || png.Length > MaximumPngBytes) throw new InvalidOperationException("Encoded image exceeds artwork bound.");
            var pngHash = NativeEncounterMapObserver.Hash(png);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, key + ".png");
            var manifest = Path.Combine(directory, key + ".json");
            if (disposed) return new CacheResult();
            // Image precedes manifest; a crash cannot advertise an incomplete image. Temp files
            // are deliberately ignored by restart validation, with no game/profile file touched.
            CommitNew(path, png);
            var metadata = new { version = 1, key, pngHash, width, height, file = key + ".png", fullCanvas = true, alpha = "straight", recipe = "unpacked-fullcanvas-blit-v1", transfer = linearColorSpace ? "linear-to-srgb" : "gamma-preserved", visualOrientationVerified = false, calibrationJson };
            CommitNew(manifest, System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata)));
            return ReadManifest(manifest, false);
        }
        catch (Exception exception) { return new CacheResult { Key = key, Error = exception.GetType().Name + ": " + exception.Message }; }
    }

    private static void CommitNew(string destination, byte[] bytes)
    {
        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length != bytes.Length || NativeEncounterMapObserver.Hash(File.ReadAllBytes(destination)) != NativeEncounterMapObserver.Hash(bytes))
                throw new IOException("Existing cache key has different content; refusing replacement.");
            return;
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private CacheResult ReadExisting()
    {
        try
        {
            if (!Directory.Exists(directory)) return new CacheResult();
            var manifest = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Take(64).FirstOrDefault();
            return manifest == null ? new CacheResult() : ReadManifest(manifest, true);
        }
        catch (Exception exception) { return new CacheResult { Error = "Existing cache: " + exception.GetType().Name + ": " + exception.Message }; }
    }

    private CacheResult ReadManifest(string manifest, bool existing)
    {
        if (new FileInfo(manifest).Length > 64 * 1024) throw new InvalidDataException("Cache manifest exceeds bound.");
        var data = JObject.Parse(File.ReadAllText(manifest));
        var key = (string?)data["key"] ?? string.Empty;
        if (key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid artwork key.");
        var calibrationJson = (string?)data["calibrationJson"] ?? string.Empty;
        if (NativeEncounterMapObserver.Hash(System.Text.Encoding.UTF8.GetBytes(calibrationJson)) != key)
            throw new InvalidDataException("Calibration hash mismatch.");
        var width = (int?)data["width"] ?? 0;
        var height = (int?)data["height"] ?? 0;
        if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension) throw new InvalidDataException("Invalid dimensions.");
        var path = Path.Combine(directory, key + ".png");
        if (new FileInfo(path).Length > MaximumPngBytes) throw new InvalidDataException("Cache PNG exceeds bound.");
        var bytes = File.ReadAllBytes(path);
        // Reject a valid-hash image whose IHDR could otherwise allocate beyond our decoded bound.
        if (bytes.Length < 33 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71
            || ReadBigEndian(bytes, 16) != width || ReadBigEndian(bytes, 20) != height)
            throw new InvalidDataException("PNG header dimensions disagree with manifest.");
        var hash = NativeEncounterMapObserver.Hash(bytes);
        if (hash != (string?)data["pngHash"]) throw new InvalidDataException("Cache image hash mismatch.");
        return new CacheResult { Key = key, Width = width, Height = height, Png = bytes, PngHash = hash, Path = path, Existing = existing };
    }

    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private void Record(string kind, object value)
    {
        if (disposed) return;
        try { sink.Record(kind, value); }
        catch (Exception exception) { Status = "cache diagnostic sink failed: " + exception.GetType().Name; }
    }
    private void ReportFailure(string area, Exception exception)
    {
        if (disposed) return;
        Status = area + " failed";
        // Even request-callback diagnostics are detached and delivered through main-thread Tick.
        pendingFailure = area + ": " + exception.GetType().Name + ": " + exception.Message;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // No blocking WaitAllRequests/readback on stop. An outstanding request owns at most one
        // 2048px target, released by its guarded completion callback, even after stop.
        if (readbackPending)
        {
            try { log("Map probe stopped with one GPU lease awaiting completion; callback only releases owned resources."); }
            catch { /* Logging cannot prevent cleanup. */ }
        }
        // Worker exceptions are converted to CacheResult. Detached IO/encoding may finish, but no
        // stopped sink or destroyed Unity object is touched and no new capture starts.
        worker = null;
    }

    private sealed class CacheResult
    {
        public string Key = string.Empty, PngHash = string.Empty, Path = string.Empty;
        public string? Error;
        public byte[]? Png;
        public int Width, Height;
        public bool Existing;
    }

    private sealed class GpuLease : IDisposable
    {
        public Sprite? BorrowedSprite;
        public RenderTexture? Target;
        public void Dispose()
        {
            if (Target != null) { Target.Release(); Object.Destroy(Target); Target = null; }
            BorrowedSprite = null;
        }
    }
}
