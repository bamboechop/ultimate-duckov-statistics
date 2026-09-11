using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace ArtifactAudit;

public static class ArtifactPathAudit
{
    public static int Audit(IEnumerable<string> inputs, IReadOnlyList<string> forbidden, string? expectedVersion = null)
    {
        var paths = inputs.SelectMany(path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            : File.Exists(path) ? [path] : throw new FileNotFoundException("Audit input missing.", path)).ToArray();
        if (paths.Length == 0) throw new ArgumentException("No artifact files to audit.");
        var failures = new List<string>();
        foreach (var path in paths)
        {
            var bytes = File.ReadAllBytes(path);
            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                CheckPng(bytes, (payload, source) =>
                {
                    Check(Encoding.UTF8.GetString(payload), path, source);
                    Check(Encoding.Unicode.GetString(payload), path, source);
                    if (payload.Length > 1) Check(Encoding.Unicode.GetString(payload, 1, payload.Length - 1), path, source);
                });
                continue;
            }
            var metadataFile = Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                               || Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase);
            Check(Encoding.UTF8.GetString(bytes), path, "UTF-8 bytes", !metadataFile);
            Check(Encoding.Unicode.GetString(bytes), path, "UTF-16 bytes", !metadataFile);
            // A UTF-16 string can begin at either byte alignment inside a PE section.
            if (bytes.Length > 1) Check(Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1), path, "UTF-16 odd bytes", !metadataFile);
            using var stream = new MemoryStream(bytes, writable: false);
            if (Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
                CheckDocuments(provider.GetMetadataReader(), path);
            }
            else if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) throw new BadImageFormatException("Expected managed UDS artifact.", path);
                foreach (var entry in pe.ReadDebugDirectory())
                {
                    if (entry.Type == DebugDirectoryEntryType.CodeView)
                        CheckIdentity(pe.ReadCodeViewDebugDirectoryData(entry).Path, path, "PE CodeView");
                    if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                    {
                        using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                        CheckDocuments(provider.GetMetadataReader(), path);
                    }
                }
                var metadata = pe.GetMetadataReader();
                var block = pe.GetMetadata();
                var strings = block.GetContent(metadata.GetHeapMetadataOffset(HeapIndex.String), metadata.GetHeapSize(HeapIndex.String));
                Check(Encoding.UTF8.GetString(strings.AsSpan()), path, "PE string heap");
                var userStrings = block.GetContent(metadata.GetHeapMetadataOffset(HeapIndex.UserString), metadata.GetHeapSize(HeapIndex.UserString));
                for (var offset = 1; offset < userStrings.Length;)
                {
                    var first = userStrings[offset];
                    if (first == 0) { offset++; continue; }
                    Check(metadata.GetUserString(MetadataTokens.UserStringHandle(offset)), path, "PE user string");
                    var prefix = (first & 0x80) == 0 ? 1 : (first & 0xc0) == 0x80 ? 2 : 4;
                    var length = prefix == 1 ? first : prefix == 2 ? ((first & 0x3f) << 8) | userStrings[offset + 1]
                        : ((first & 0x1f) << 24) | (userStrings[offset + 1] << 16) | (userStrings[offset + 2] << 8) | userStrings[offset + 3];
                    offset = checked(offset + prefix + length);
                }
                if (expectedVersion != null && InformationalVersion(metadata) != expectedVersion)
                    failures.Add($"{Path.GetFileName(path)}: assembly informational version differs from {expectedVersion}.");
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    Check(metadata.GetString(type.Namespace), path, "PE namespace");
                    Check(metadata.GetString(type.Name), path, "PE type");
                }
            }

        }
        if (failures.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, failures.Distinct(StringComparer.Ordinal)));
        return paths.Length;

        void CheckDocuments(MetadataReader metadata, string path)
        {
            foreach (var handle in metadata.Documents)
                CheckIdentity(metadata.GetString(metadata.GetDocument(handle).Name), path, "PDB document");
            foreach (var handle in metadata.CustomDebugInformation)
            {
                var blob = metadata.GetBlobBytes(metadata.GetCustomDebugInformation(handle).Value);
                Check(Encoding.UTF8.GetString(blob), path, "PDB custom debug information");
                Check(Encoding.Unicode.GetString(blob), path, "PDB custom debug information");
            }
        }

        void CheckIdentity(string identity, string path, string source)
        {
            var normalized = identity.Replace('\\', '/');
            // Keep useful virtual source identities; a CodeView basename is also portable.
            if ((normalized.StartsWith('/') && !normalized.StartsWith("/_/uds/", StringComparison.Ordinal))
                || Regex.IsMatch(normalized, "^[A-Za-z]:/", RegexOptions.CultureInvariant))
                failures.Add($"{Path.GetFileName(path)}: absolute builder identity in {source}: {identity}");
            Check(identity, path, source);
        }

        void Check(string text, string path, string source, bool scanGenericUnix = true)
        {
            // Scan raw blobs as well as decoded metadata: PDB document components are not contiguous in the bytes.
            foreach (Match match in Regex.Matches(text,
                @"(?i)(?:(?<![a-z0-9])[a-z]:[\\/]|\\\\[a-z0-9_.-]+[\\/]|/(?:Users|home|root|tmp|builds|workspace|agent|mnt|opt|var/tmp)/)[^\x00-\x1f<>""|]{1,240}",
                RegexOptions.CultureInvariant))
                failures.Add($"{Path.GetFileName(path)}: absolute machine path in {source}: {match.Value}");
            // Recognize other Unix roots without treating URLs, relative paths or documented <game>/%VAR% placeholders as roots.
            if (scanGenericUnix) foreach (Match match in Regex.Matches(text,
                @"(?<![\w:/.\\>%\-])/(?!/|_/uds/)[a-zA-Z0-9_.-]+(?:/[a-zA-Z0-9_.-]+)+",
                RegexOptions.CultureInvariant))
                    failures.Add($"{Path.GetFileName(path)}: absolute Unix path in {source}: {match.Value}");
            foreach (var token in forbidden.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var variants = new[] { token, token.Replace('\\', '/'), token.Replace("\\", "\\\\", StringComparison.Ordinal) };
                if (variants.Any(value => Regex.IsMatch(text, @"(?<![\w])" + Regex.Escape(value) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                    failures.Add($"{Path.GetFileName(path)}: forbidden builder identity in {source}.");
            }
        }

    }

    // PNG pixel/palette bytes are not text. Inspect metadata separately, inflating the
    // compressed text/profile chunks before checking them for builder identities.
    private static void CheckPng(byte[] bytes, Action<byte[], string> check)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.AsSpan().StartsWith(signature)) throw new InvalidDataException("Invalid PNG signature.");
        var offset = signature.Length;
        var hasImageData = false;
        while (bytes.Length - offset >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("Truncated PNG chunk.");
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (offset == signature.Length && (type != "IHDR" || length != 13))
                throw new InvalidDataException("Missing PNG image header.");
            var payload = bytes.AsSpan(offset + 8, (int)length).ToArray();
            offset += 12 + (int)length;
            if (type == "IEND")
            {
                if (length != 0 || !hasImageData || offset != bytes.Length)
                    throw new InvalidDataException("Invalid PNG image end or trailing data.");
                return;
            }
            if (type == "IDAT") { hasImageData = true; continue; }
            if (type is "IHDR" or "PLTE") continue;
            if (type is "zTXt" or "iCCP")
            {
                var separator = Array.IndexOf(payload, (byte)0);
                if (separator < 1 || separator + 1 >= payload.Length || payload[separator + 1] != 0)
                    throw new InvalidDataException($"Invalid PNG {type} compression header.");
                check(payload[..separator], $"PNG {type} keyword");
                check(Inflate(payload[(separator + 2)..]), $"PNG {type} decoded metadata");
            }
            else if (type == "iTXt")
            {
                var separator = Array.IndexOf(payload, (byte)0);
                if (separator < 1 || separator + 2 >= payload.Length || payload[separator + 1] > 1 || payload[separator + 2] != 0)
                    throw new InvalidDataException("Invalid PNG iTXt compression header.");
                var languageEnd = Array.IndexOf(payload, (byte)0, separator + 3);
                var translatedEnd = languageEnd < 0 ? -1 : Array.IndexOf(payload, (byte)0, languageEnd + 1);
                if (translatedEnd < 0) throw new InvalidDataException("Invalid PNG iTXt text header.");
                check(payload[..(translatedEnd + 1)], "PNG iTXt header");
                var text = payload[(translatedEnd + 1)..];
                check(payload[separator + 1] == 1 ? Inflate(text) : text, "PNG iTXt decoded metadata");
            }
            else check(payload, $"PNG {type} metadata");
        }
        throw new InvalidDataException("Missing PNG image end.");

        static byte[] Inflate(byte[] compressed)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = inflater.Read(buffer)) > 0)
            {
                if (output.Length + read > 16 * 1024 * 1024)
                    throw new InvalidDataException("PNG metadata exceeds the 16 MiB audit limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    private static string? InformationalVersion(MetadataReader metadata)
    {
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Reflection"
                || metadata.GetString(type.Name) != "AssemblyInformationalVersionAttribute") continue;
            var value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) throw new BadImageFormatException("Invalid assembly version attribute.");
            return value.ReadSerializedString();
        }
        return null;
    }
}
