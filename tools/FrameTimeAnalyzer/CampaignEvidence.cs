using System.Security.Cryptography;
using System.Text.Json;

namespace FrameTimeAnalyzer;

internal static class CampaignEvidence
{
    private static readonly string[] Configurations = ["B", "D"];
    private static readonly string[] CandidateDlls = ["UltimateDuckovStatistics.Core.dll", "UltimateDuckovStatistics.dll"];
    // Opt-in strict evidence checks leave historical M8.1 inputs and results unchanged.
    public static IReadOnlyList<string> Validate(string campaignPath, IReadOnlyList<string> files)
    {
        using var campaignDocument = JsonDocument.Parse(File.ReadAllText(campaignPath));
        var campaign = campaignDocument.RootElement;
        Require(campaign.GetProperty("SchemaVersion").GetInt32() == 1, "Unknown campaign format.");
        Require(campaign.GetProperty("DllHashes").EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(CandidateDlls), "Campaign must bind both candidate DLLs.");
        var matrix = campaign.GetProperty("Matrix");
        var scenarios = matrix.GetProperty("Scenarios").EnumerateArray().ToDictionary(value => Text(value, "Id"), StringComparer.Ordinal);
        var expectedCampaignHash = Hash(campaignPath);
        var seen = new HashSet<(string Configuration, string Scenario, int Run)>();
        foreach (var path in files)
        {
            var stem = path.EndsWith(".capframex.json", StringComparison.OrdinalIgnoreCase)
                ? path[..^".capframex.json".Length] : Path.ChangeExtension(path, null);
            using var sidecar = JsonDocument.Parse(File.ReadAllText(stem + ".capture.json"));
            var metadata = sidecar.RootElement;
            Require(metadata.GetProperty("SchemaVersion").GetInt32() >= 6, "Campaign requires current control sidecars.");
            Require(Text(metadata, "CampaignSha256") == expectedCampaignHash, "Capture belongs to a different frozen campaign.");
            Require(Text(metadata, "CandidateSourceCommit") == Text(campaign, "SourceCommit"), "Capture candidate source differs.");
            Require(Text(metadata, "BuildLabel") == "production", "Diagnostic builds cannot enter acceptance cells.");
            var configuration = Text(metadata, "Configuration");
            Require(configuration is "B" or "D", "Campaign accepts only matched Harmony-only B and ordinary candidate D.");
            var scenario = Text(metadata, "Scenario");
            Require(scenarios.TryGetValue(scenario, out var cell), "Capture scenario is not declared in the campaign.");
            var run = metadata.GetProperty("Run").GetInt32();
            Require(run > 0 && seen.Add((configuration, scenario, run)), "Duplicate or invalid capture run.");
            Require(metadata.GetProperty("CaptureSeconds").GetInt32() == matrix.GetProperty("CaptureSeconds").GetInt32(), "Capture duration differs from frozen matrix.");
            Require(Text(metadata, "ActionKind") == Text(cell, "Kind"), "Capture activity differs from frozen matrix.");
            Require(EqualNumberOrNull(metadata.GetProperty("RequestedActionStartSeconds"), cell.GetProperty("Start"))
                && EqualNumberOrNull(metadata.GetProperty("RequestedActionEndSeconds"), cell.GetProperty("End")), "Action timing differs from frozen matrix.");
            var mods = metadata.GetProperty("LoadedMods").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal).ToArray();
            var expectedMods = configuration == "D" ? new[] { "HarmonyLoadMod", "UltimateDuckovStatistics" } : new[] { "HarmonyLoadMod" };
            Require(mods.SequenceEqual(expectedMods), "Loaded mods differ from the controlled configuration.");
            Require(metadata.GetProperty("UdsActivationObserved").GetBoolean() == (configuration == "D"), "Current-launch UDS activation differs.");
            if (configuration == "D")
            {
                Require(Text(metadata, "DeployedUdsInfoVersion") == Text(campaign, "Version"), "Candidate version differs.");
                var deployed = metadata.GetProperty("DeployedFiles").EnumerateArray().ToDictionary(value => Text(value, "Name"), value => Text(value, "Sha256"), StringComparer.Ordinal);
                foreach (var expected in campaign.GetProperty("DllHashes").EnumerateObject())
                    Require(deployed.TryGetValue(expected.Name, out var actual) && actual == expected.Value.GetString(), "Deployed candidate DLL differs from frozen campaign: " + expected.Name);
            }
            Require(Hash(stem + ".csv") == Text(metadata, "RawCsvSha256"), "Raw CSV hash differs from sidecar.");
            Require(Hash(stem + ".capframex.json") == Text(metadata, "RawCapFrameXJsonSha256"), "Raw CapFrameX JSON hash differs from sidecar.");
        }
        var requiredRuns = matrix.GetProperty("Repetitions").GetInt32();
        return scenarios.Keys.SelectMany(scenario => Configurations.Select(configuration => (configuration, scenario)))
            .Where(cell => seen.Count(value => value.Configuration == cell.configuration && value.Scenario == cell.scenario) < requiredRuns)
            .Select(cell => $"Not exercised: {cell.configuration}/{cell.scenario} needs {requiredRuns} valid captures.").ToArray();
    }

    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString()
        ?? throw new InvalidDataException("Missing evidence value: " + property);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool EqualNumberOrNull(JsonElement left, JsonElement right) =>
        left.ValueKind == JsonValueKind.Null || right.ValueKind == JsonValueKind.Null
            ? left.ValueKind == right.ValueKind : left.GetDouble() == right.GetDouble();
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}
