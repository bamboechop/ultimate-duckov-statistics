using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FrameTimeAnalyzer;

namespace UltimateDuckovStatistics.Tests;

public sealed class CampaignEvidenceTests
{
    [Fact]
    public void ExactArtifactEvidenceReportsMissingCellsWithoutInventingAcceptance()
    {
        using var fixture = new CampaignFixture();
        var files = new List<string>();
        foreach (var configuration in new[] { "B", "D" })
            for (var run = 1; run <= 3; run++) files.Add(fixture.Capture(configuration, run));
        Assert.Empty(CampaignEvidence.Validate(fixture.CampaignPath, files));
        Assert.Single(CampaignEvidence.Validate(fixture.CampaignPath, files[..^1]));
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("campaign")]
    [InlineData("source")]
    [InlineData("diagnostic")]
    [InlineData("timing")]
    [InlineData("mods")]
    [InlineData("raw")]
    public void RefusesMixedOrChangedCaptureEvidence(string change)
    {
        using var fixture = new CampaignFixture();
        var capture = fixture.Capture("D", 1);
        var sidecarPath = Path.ChangeExtension(capture, ".capture.json");
        var metadata = JsonNode.Parse(File.ReadAllText(sidecarPath))!;
        switch (change)
        {
            case "binary": metadata["DeployedFiles"]![0]!["Sha256"] = new string('c', 64); break;
            case "campaign": metadata["CampaignSha256"] = new string('c', 64); break;
            case "source": metadata["CandidateSourceCommit"] = new string('c', 40); break;
            case "diagnostic": metadata["BuildLabel"] = "diagnostic"; break;
            case "timing": metadata["RequestedActionStartSeconds"] = 7; break;
            case "mods": metadata["LoadedMods"]!.AsArray().Add("ForeignMod"); break;
            case "raw": File.AppendAllText(capture, "changed"); break;
        }
        File.WriteAllText(sidecarPath, metadata.ToJsonString());
        Assert.Throws<InvalidDataException>(() => CampaignEvidence.Validate(fixture.CampaignPath, [capture]));
    }

    [Fact]
    public void DuplicateCaptureCannotSupplyRequiredRepetitions()
    {
        using var fixture = new CampaignFixture();
        var capture = fixture.Capture("B", 1);
        Assert.Throws<InvalidDataException>(() => CampaignEvidence.Validate(fixture.CampaignPath, [capture, capture]));
    }

    private sealed class CampaignFixture : IDisposable
    {
        private static readonly string[] CandidateMods = ["HarmonyLoadMod", "UltimateDuckovStatistics"];
        private static readonly string[] BaselineMods = ["HarmonyLoadMod"];
        private readonly TemporaryDirectory directory = new();
        public string CampaignPath { get; }
        public CampaignFixture()
        {
            CampaignPath = Path.Combine(directory.Path, "campaign.json");
            File.WriteAllText(CampaignPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                SourceCommit = new string('a', 40),
                Version = "1.0.0-rc.1",
                DllHashes = new Dictionary<string, string> { ["UltimateDuckovStatistics.dll"] = new string('b', 64), ["UltimateDuckovStatistics.Core.dll"] = new string('d', 64) },
                Matrix = new { Repetitions = 3, CaptureSeconds = 30, Scenarios = new[] { new { Id = "idle", Kind = "idle", Start = (double?)null, End = (double?)null } } }
            }));
        }
        public string Capture(string configuration, int run)
        {
            var stem = Path.Combine(directory.Path, configuration + run);
            File.WriteAllText(stem + ".csv", "MsBetweenPresents\n5\n");
            File.WriteAllText(stem + ".capframex.json", "{}");
            File.WriteAllText(stem + ".capture.json", JsonSerializer.Serialize(new
            {
                SchemaVersion = 6,
                CandidateSourceCommit = new string('a', 40),
                CampaignSha256 = Hash(CampaignPath),
                Configuration = configuration,
                BuildLabel = "production",
                Scenario = "idle",
                Run = run,
                CaptureSeconds = 30,
                ActionKind = "idle",
                RequestedActionStartSeconds = (double?)null,
                RequestedActionEndSeconds = (double?)null,
                LoadedMods = configuration == "D" ? CandidateMods : BaselineMods,
                UdsActivationObserved = configuration == "D",
                DeployedUdsInfoVersion = "1.0.0-rc.1",
                DeployedFiles = new[] { new { Name = "UltimateDuckovStatistics.dll", Sha256 = new string('b', 64) }, new { Name = "UltimateDuckovStatistics.Core.dll", Sha256 = new string('d', 64) } },
                RawCsvSha256 = Hash(stem + ".csv"),
                RawCapFrameXJsonSha256 = Hash(stem + ".capframex.json")
            }));
            return stem + ".csv";
        }
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        public void Dispose() => directory.Dispose();
    }
}
