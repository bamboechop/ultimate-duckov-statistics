using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UltimateDuckovStatistics.Encounters;

// Main-thread map artwork owner used by the retained Runs view and diagnostic preview. Workers only read/hash detached files.
internal sealed class EncounterMapPresentationAssets : IDisposable
{
    private readonly string directory;
    private readonly Action<string> log;
    private Task<LoadedBytes>? pending;
    private string requested = string.Empty;
    private string inFlight = string.Empty;
    private bool disposed;

    public EncounterMapPresentationAssets(string mapsDirectory, Action<string> log)
    {
        directory = Path.Combine(Path.GetFullPath(mapsDirectory), "maps-cache");
        this.log = log;
    }

    public Texture2D? Texture { get; private set; }
    public EncounterMapCalibration? Calibration { get; private set; }
    public string CalibrationJson { get; private set; } = string.Empty;
    public string LoadedKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = "No satellite image selected";

    public void Request(string calibrationId)
    {
        if (disposed || requested == calibrationId) return;
        requested = calibrationId;
        ReleaseTexture();
        Calibration = null;
        CalibrationJson = string.Empty;
        LoadedKey = string.Empty;
        Status = string.IsNullOrEmpty(requested) ? "No satellite image available" : "Loading recorded map artwork";
        BeginPending();
    }

    public void Tick()
    {
        if (disposed || pending == null || !pending.IsCompleted) return;
        var completed = pending;
        var key = inFlight;
        pending = null;
        inFlight = string.Empty;
        try
        {
            var data = completed.GetAwaiter().GetResult();
            if (key != requested) { BeginPending(); return; }
            if (data.Error != null) { Status = "No satellite image: " + data.Error; return; }
            var calibration = ParseCalibration(data.CalibrationJson);
            Calibration = calibration;
            CalibrationJson = data.CalibrationJson;
            if (!calibration.Available || calibration.Hidden || calibration.NoSignal)
            {
                Status = "No satellite image: native map is hidden, has no signal, or is unavailable";
                return;
            }
            Texture2D? owned = null;
            try
            {
                owned = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "UDS encounter history map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!ImageConversion.LoadImage(owned, data.Png!, true) || owned.width != data.Width || owned.height != data.Height)
                    throw new InvalidDataException("Owned map texture decode failed.");
                Texture = owned;
                owned = null;
                LoadedKey = key;
                Status = "Recorded source artwork; native MapSprite material appearance unverified";
            }
            finally { if (owned != null) Object.Destroy(owned); }
        }
        catch (Exception exception)
        {
            Status = "No satellite image: " + exception.GetType().Name + ": " + exception.Message;
            try { log(Status); } catch { /* Viewer diagnostics cannot escape into gameplay. */ }
        }
    }

    private void BeginPending()
    {
        if (disposed || pending != null || string.IsNullOrEmpty(requested)) return;
        if (requested.Length != 64 || requested.Any(c => !Uri.IsHexDigit(c))) { Status = "No satellite image: invalid artwork identity"; return; }
        var key = requested;
        inFlight = key;
        pending = Task.Run(() => Read(key));
    }

    private LoadedBytes Read(string key)
    {
        try
        {
            var manifestPath = Path.Combine(directory, key + ".json");
            if (!File.Exists(manifestPath)) return new LoadedBytes { Error = "cache entry missing" };
            var manifest = JObject.Parse(System.Text.Encoding.UTF8.GetString(ReadBounded(manifestPath, 512 * 1024)));
            if ((string?)manifest["key"] != key) throw new InvalidDataException("Manifest key mismatch.");
            var calibration = (string?)manifest["calibrationJson"] ?? string.Empty;
            if (NativeEncounterMapObserver.Hash(System.Text.Encoding.UTF8.GetBytes(calibration)) != key)
                throw new InvalidDataException("Calibration hash mismatch.");
            var width = (int?)manifest["width"] ?? 0;
            var height = (int?)manifest["height"] ?? 0;
            if (width < 1 || height < 1 || width > 2048 || height > 2048) throw new InvalidDataException("Image dimensions exceed bound.");
            var imagePath = Path.Combine(directory, key + ".png");
            if (!File.Exists(imagePath)) return new LoadedBytes { Error = "image bytes missing" };
            var png = ReadBounded(imagePath, 20 * 1024 * 1024);
            if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71
                || BigEndian(png, 16) != width || BigEndian(png, 20) != height) throw new InvalidDataException("PNG header mismatch.");
            if (NativeEncounterMapObserver.Hash(png) != (string?)manifest["pngHash"]) throw new InvalidDataException("PNG hash mismatch.");
            if ((string?)manifest["recipe"] != "unpacked-fullcanvas-blit-v1") throw new InvalidDataException("Unsupported artwork recipe.");
            return new LoadedBytes { Width = width, Height = height, Png = png, CalibrationJson = calibration };
        }
        catch (Exception exception) { return new LoadedBytes { Error = exception.GetType().Name + ": " + exception.Message }; }
    }

    public static EncounterMapCalibration ParseCalibration(string json)
    {
        var data = JObject.Parse(json);
        return new EncounterMapCalibration
        {
            MapId = (string?)data["MapId"] ?? string.Empty,
            CenterX = Coordinate(data, "worldCenter", 0), CenterZ = Coordinate(data, "worldCenter", 2),
            WorldSize = (double?)data["imageWorldSize"] ?? double.NaN,
            OffsetX = Coordinate(data, "offset", 0), OffsetY = Coordinate(data, "offset", 1),
            Combined = (bool?)data["combined"] == true,
            CombinedCenterX = Coordinate(data, "combinedCenter", 0), CombinedCenterY = Coordinate(data, "combinedCenter", 1),
            CombinedSize = (double?)data["combinedSize"] ?? double.NaN,
            Available = (bool?)data["available"] == true, Hidden = (bool?)data["hide"] == true, NoSignal = (bool?)data["noSignal"] == true
        };
    }

    private static double Coordinate(JObject data, string property, int index)
        => data[property] is JArray array && array.Count > index ? (double?)array[index] ?? double.NaN : double.NaN;
    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Cache file exceeds bound.");
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0) throw new EndOfStreamException("Cache file changed during read.");
            offset += count;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Cache file changed during read.");
        return bytes;
    }
    private static int BigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private void ReleaseTexture() { if (Texture != null) Object.Destroy(Texture); Texture = null; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseTexture();
        pending = null; // A detached reader may finish; it cannot create Unity objects or publish into this owner.
        Calibration = null;
    }

    private sealed class LoadedBytes
    {
        public int Width, Height;
        public byte[]? Png;
        public string CalibrationJson = string.Empty;
        public string? Error;
    }
}
