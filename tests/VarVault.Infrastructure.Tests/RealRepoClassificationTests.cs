using System.IO;
using System.Text;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// D: real-data probe — runs the central-directory reader + classification engine over the user's
/// actual var corpus, proving the pipeline classifies real content and produces sane primary types.
/// No-ops when the corpus is absent (other machines/CI). (IDX-5 acceptance on a real corpus.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class RealRepoClassificationTests
{
    private const string Repo = @"D:\VarVault_test_repo\Vam_Installer_notSameContentFiles1";

    [Fact]
    public void Classifies_real_vars_into_known_types_with_a_primary()
    {
        if (!Directory.Exists(Repo))
            return;

        var vars = Directory.GetFiles(Repo, "*.var");
        Assert.NotEmpty(vars);

        var classifiedCount = 0;
        var totalTypeHits = new Dictionary<ContentType, int>();

        foreach (var path in vars)
        {
            var read = ZipCentralDirectoryReader.Read(path);
            if (read.IsFailure)
                continue; // corrupt/edge var — exercised elsewhere

            var names = read.Value.Select(e => e.DecodedNameBestEffort);
            var classification = ContentClassificationEngine.Classify(names);

            if (classification.Counts.Count > 0)
            {
                classifiedCount++;
                Assert.NotEqual(ContentType.Unknown, classification.PrimaryType);
                foreach (var (type, count) in classification.Counts)
                    totalTypeHits[type] = totalTypeHits.GetValueOrDefault(type) + count;
            }
        }

        // The corpus is real VaM content — the pipeline must classify a meaningful share of it.
        Assert.True(classifiedCount > 0, "expected at least some real vars to classify into known content types");
    }

    [Fact]
    public void Encoding_detection_runs_over_the_real_corpus_without_false_crashes()
    {
        if (!Directory.Exists(Repo))
            return;

        var vars = Directory.GetFiles(Repo, "*.var");
        Assert.NotEmpty(vars);

        foreach (var path in vars)
        {
            var read = ZipCentralDirectoryReader.Read(path);
            if (read.IsFailure)
                continue;

            var health = Domain.Content.EncodingHealthEngine.Detect(read.Value);
            // Detection must be deterministic and total (never throw) across the whole corpus.
            var again = Domain.Content.EncodingHealthEngine.Detect(read.Value);
            Assert.Equal(health.Health, again.Health);
            Assert.Equal(health.DetectedCodepage, again.DetectedCodepage);
        }
    }
}
