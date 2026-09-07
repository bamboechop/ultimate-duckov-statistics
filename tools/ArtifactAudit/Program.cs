using ArtifactAudit;

if (args.Length == 0) throw new ArgumentException("Supply PE, portable PDB, or package files/directories; optional --forbid <builder path or username>.");
var forbidden = new List<string>();
var inputs = new List<string>();
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--forbid" && index + 1 < args.Length) forbidden.Add(args[++index]);
    else inputs.Add(args[index]);
}
var count = ArtifactPathAudit.Audit(inputs, forbidden);
Console.WriteLine($"PASS: {count} files; PE debug identities, portable PDB documents, UTF-8/UTF-16 payloads.");
