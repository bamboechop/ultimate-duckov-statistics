using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace ArtifactAudit;

public static class ArtifactPathAudit
{
    public static int Audit(IEnumerable<string> inputs, IReadOnlyList<string> forbidden)
    {
        var paths = inputs.SelectMany(path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            : File.Exists(path) ? [path] : throw new FileNotFoundException("Audit input missing.", path)).ToArray();
        if (paths.Length == 0) throw new ArgumentException("No artifact files to audit.");
        var failures = new List<string>();
        foreach (var path in paths)
        {
            var bytes = File.ReadAllBytes(path);
            Check(Encoding.UTF8.GetString(bytes), path, "UTF-8 bytes");
            Check(Encoding.Unicode.GetString(bytes), path, "UTF-16 bytes");
            // A UTF-16 string can begin at either byte alignment inside a PE section.
            if (bytes.Length > 1) Check(Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1), path, "UTF-16 odd bytes");
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

        void Check(string text, string path, string source)
        {
            // Scan raw blobs as well as decoded metadata: PDB document components are not contiguous in the bytes.
            foreach (Match match in Regex.Matches(text,
                @"(?i)(?:(?<![a-z0-9])[a-z]:[\\/]|\\\\[a-z0-9_.-]+[\\/]|/(?:Users|home|root|tmp|builds|workspace|agent|mnt|opt|var/tmp)/)[^\x00-\x1f<>""|]{1,240}",
                RegexOptions.CultureInvariant))
                failures.Add($"{Path.GetFileName(path)}: absolute machine path in {source}: {match.Value}");
            foreach (var token in forbidden.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var variants = new[] { token, token.Replace('\\', '/'), token.Replace("\\", "\\\\", StringComparison.Ordinal) };
                if (variants.Any(value => Regex.IsMatch(text, @"(?<![\w])" + Regex.Escape(value) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                    failures.Add($"{Path.GetFileName(path)}: forbidden builder identity in {source}.");
            }
        }

    }
}
