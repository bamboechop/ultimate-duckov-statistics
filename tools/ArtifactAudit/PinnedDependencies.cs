using System.Security.Cryptography;

namespace ArtifactAudit;

public static class PinnedDependencies
{
    public static bool IsPinnedManaged(string path, byte[] bytes)
    {
        var hash = Path.GetFileName(path) switch
        {
            "UdsPrototype.SQLiteRaw.Core.dll" => "8db3fbec9933c6c95b8253439d5fbe93c109484a79bb44054b050df75fde9880",
            "UdsPrototype.SQLiteRaw.Provider.dll" => "b67049204702ef53ca9799d5416fa1ce344e2f984bbf7b079bf2737a00c55cc7",
            _ => null
        };
        if (hash == null) return false;
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite provider differs from the pinned upstream binary.");
        return true;
    }

    public static bool IsPinnedNative(string path, byte[] bytes)
    {
        if (!Path.GetFileName(path).Equals("sqlite3.dll", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals("ab57d0437795ecc757cb693f32ea224173fa9856594d95cfa6b5033e645cd1ec", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite native dependency differs from the pinned upstream binary.");
        return true;
    }
}
