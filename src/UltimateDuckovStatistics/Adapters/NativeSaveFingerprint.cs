using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UltimateDuckovStatistics.Adapters;

// Always observes fresh bytes. No timestamp/length-only identity cache: a restored
// save may retain both. The caller still rejects metadata changes during the read.
internal static class NativeSaveFingerprint
{
    private static readonly Regex SaveTimePattern = new(
        "\\\"SaveTime\\\"\\s*:\\s*\\{[^{}]*?\\\"value\\\"\\s*:\\s*(-?\\d+)",
        RegexOptions.CultureInvariant);

    internal static (string Hash, long? SaveTime) Read(Stream stream, bool useNativeHash = true)
    {
        // The installed Mono SHA256CryptoServiceProvider delegates to managed SHA256.
        // Use Windows CNG directly, with the existing managed algorithm as fallback.
        try
        {
            using var hash = new FingerprintHash(useNativeHash);
            return Read(stream, hash);
        }
        catch (Exception exception) when (useNativeHash && exception is
                   DllNotFoundException or EntryPointNotFoundException or CryptographicException)
        {
            stream.Position = 0;
            using var hash = new FingerprintHash(useNativeHash: false);
            return Read(stream, hash);
        }
    }

    private static (string Hash, long? SaveTime) Read(Stream stream, FingerprintHash hash)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var scanner = new SaveTimeScanner();
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                hash.Append(buffer, count);
                scanner.Feed(buffer.AsSpan(0, count));
            }
            var result = hash.Finish();
            var saveTime = scanner.Value;
            if (!saveTime.HasValue)
            {
                // Nonstandard encodings/entries retain the original reader semantics.
                // Normal native UTF-8 SaveTime entries fit the bounded fast path.
                stream.Position = 0;
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
                saveTime = ParseSaveTime(reader.ReadToEnd());
            }
            return (BitConverter.ToString(result).Replace("-", string.Empty).ToLowerInvariant(), saveTime);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static long? ParseSaveTime(string text)
    {
        var match = SaveTimePattern.Match(text);
        return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private sealed class SaveTimeScanner
    {
        private static readonly byte[] Key = Encoding.ASCII.GetBytes("\"SaveTime\"");
        private readonly byte[] candidate = new byte[4096];
        private int count;
        private bool stopped;
        internal long? Value { get; private set; }

        internal void Feed(ReadOnlySpan<byte> bytes)
        {
            if (stopped) return;
            while (!bytes.IsEmpty)
            {
                if (count == 0)
                {
                    var quote = bytes.IndexOf((byte)'"');
                    if (quote < 0) return;
                    bytes = bytes.Slice(quote);
                    var prefixLength = Math.Min(bytes.Length, Key.Length);
                    if (!bytes.Slice(0, prefixLength).SequenceEqual(Key.AsSpan(0, prefixLength)))
                    {
                        bytes = bytes.Slice(1);
                        continue;
                    }
                    bytes.Slice(0, prefixLength).CopyTo(candidate);
                    count = prefixLength;
                    bytes = bytes.Slice(prefixLength);
                }
                if (count < Key.Length)
                {
                    while (count < Key.Length)
                    {
                        if (bytes.IsEmpty) return;
                        if (bytes[0] != Key[count])
                        {
                            // The partial token contains no internal quote. Resume
                            // at the mismatch; a normal split key needs no fallback.
                            count = 0;
                            break;
                        }
                        candidate[count++] = bytes[0];
                        bytes = bytes.Slice(1);
                    }
                    if (count == 0) continue;
                }
                var end = bytes.IndexOf((byte)'}');
                var take = end < 0 ? bytes.Length : end + 1;
                if (take > candidate.Length - count) { stopped = true; return; }
                bytes.Slice(0, take).CopyTo(candidate.AsSpan(count));
                count += take;
                if (end < 0) return;
                Value = ParseSaveTime(Encoding.UTF8.GetString(candidate, 0, count));
                stopped = true;
                return;
            }
        }
    }

    private sealed class FingerprintHash : IDisposable
    {
        private IntPtr native;
        private readonly SHA256? managed;

        internal FingerprintHash(bool useNativeHash)
        {
            if (useNativeHash && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // BCRYPT_SHA256_ALG_HANDLE, Windows 10+. The OS allocates the hash
                // object and BCryptDestroyHash releases it even after read failure.
                var status = BCryptCreateHash(new IntPtr(0x41), out native, IntPtr.Zero, 0, IntPtr.Zero, 0, 0);
                if (status < 0) { Dispose(); Check(status); }
            }
            else managed = SHA256.Create();
        }

        internal void Append(byte[] bytes, int count)
        {
            if (managed != null) managed.TransformBlock(bytes, 0, count, null, 0);
            else Check(BCryptHashData(native, bytes, count, 0));
        }

        internal byte[] Finish()
        {
            if (managed != null)
            {
                managed.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return managed.Hash!;
            }
            var result = new byte[32];
            Check(BCryptFinishHash(native, result, result.Length, 0));
            return result;
        }

        public void Dispose()
        {
            // Never mask an in-flight read/hash exception during cleanup.
            if (native != IntPtr.Zero) { _ = BCryptDestroyHash(native); native = IntPtr.Zero; }
            managed?.Dispose();
        }

        private static void Check(int status)
        {
            if (status < 0) throw new CryptographicException("Windows SHA256 failed: " + status.ToString("X8", CultureInfo.InvariantCulture));
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptCreateHash(IntPtr algorithm, out IntPtr hash, IntPtr storage, int storageLength, IntPtr secret, int secretLength, int flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptHashData(IntPtr hash, [In] byte[] bytes, int count, int flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptFinishHash(IntPtr hash, [Out] byte[] bytes, int count, int flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptDestroyHash(IntPtr hash);
    }
}
