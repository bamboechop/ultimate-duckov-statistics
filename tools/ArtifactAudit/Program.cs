using ArtifactAudit;

if (args.Length == 0) throw new ArgumentException("Supply PE, portable PDB, or package files/directories; optional --forbid <builder path or username>.");
var forbidden = new List<string>();
var inputs = new List<string>();
string? expectedVersion = null;
var ordinaryRelease = false;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--forbid" && index + 1 < args.Length) forbidden.Add(args[++index]);
    else if (args[index] == "--version" && index + 1 < args.Length) expectedVersion = args[++index];
    else if (args[index] == "--ordinary-release") ordinaryRelease = true;
    else inputs.Add(args[index]);
}
var count = ArtifactPathAudit.Audit(inputs, forbidden, expectedVersion);
if (ordinaryRelease)
{
    foreach (var path in inputs.SelectMany(path => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)
        : path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? [path] : Array.Empty<string>()))
        OrdinaryReleaseAudit.Verify(path);
    Console.WriteLine("PASS: ordinary Release IL has no performance-diagnostic call sites.");
}
Console.WriteLine($"PASS: {count} files; PE debug identities, portable PDB documents, UTF-8/UTF-16 payloads.");
