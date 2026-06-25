using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the single source of truth for how Nexus repos map to ecosystems (ADR 0001):
/// the repo-name PREFIX is the key, never the Nexus format (Debian and Ubuntu both = apt),
/// and an unknown prefix/format resolves to "unknown" — never a silent PyPI fallback.
/// </summary>
public class NexusEcosystemsTests
{
    [Theory]
    [InlineData(Ecosystem.PyPI, "pypi")]
    [InlineData(Ecosystem.npm, "npm")]
    [InlineData(Ecosystem.NuGet, "nuget")]
    [InlineData(Ecosystem.Cargo, "cargo")]
    [InlineData(Ecosystem.Go, "go")]
    [InlineData(Ecosystem.Maven, "maven")]
    [InlineData(Ecosystem.RubyGems, "rubygems")]
    [InlineData(Ecosystem.Composer, "composer")]
    [InlineData(Ecosystem.Conan, "conan")]
    [InlineData(Ecosystem.CRAN, "cran")]
    [InlineData(Ecosystem.DartPub, "dartpub")]
    [InlineData(Ecosystem.Alpine, "alpine")]
    [InlineData(Ecosystem.Debian, "debian")]
    [InlineData(Ecosystem.Ubuntu, "ubuntu")]
    public void Prefix_is_stable_per_ecosystem(Ecosystem eco, string expectedPrefix)
        => Assert.Equal(expectedPrefix, NexusEcosystems.Prefix(eco));

    [Fact]
    public void Debian_and_Ubuntu_have_distinct_prefixes_despite_sharing_apt_format()
    {
        Assert.NotEqual(NexusEcosystems.Prefix(Ecosystem.Debian), NexusEcosystems.Prefix(Ecosystem.Ubuntu));
        Assert.Equal("apt", NexusEcosystems.Format(Ecosystem.Debian));
        Assert.Equal("apt", NexusEcosystems.Format(Ecosystem.Ubuntu));
    }

    [Theory]
    [InlineData("cran-quarantine", Ecosystem.CRAN)]
    [InlineData("debian-quarantine", Ecosystem.Debian)]
    [InlineData("ubuntu-quarantine", Ecosystem.Ubuntu)]
    [InlineData("rubygems-approved", Ecosystem.RubyGems)]
    [InlineData("maven-quarantine", Ecosystem.Maven)]
    public void FromRepoName_maps_by_prefix(string repo, Ecosystem expected)
    {
        Assert.True(NexusEcosystems.TryFromRepoName(repo, out var eco));
        Assert.Equal(expected, eco);
    }

    [Theory]
    [InlineData("maven-central")]      // a Nexus default repo — not our convention
    [InlineData("totallyunknown-quarantine")]
    [InlineData("")]
    public void FromRepoName_does_not_fall_back_to_pypi_on_unknown(string repo)
        => Assert.False(NexusEcosystems.TryFromRepoName(repo, out _));

    [Theory]
    [InlineData("maven2", Ecosystem.Maven)]   // Nexus's maven recipe is named "maven2"
    [InlineData("rubygems", Ecosystem.RubyGems)]
    [InlineData("go", Ecosystem.Go)]
    public void FromFormat_maps_known_formats(string format, Ecosystem expected)
    {
        Assert.True(NexusEcosystems.TryFromFormat(format, out var eco));
        Assert.Equal(expected, eco);
    }

    [Fact]
    public void FromFormat_unknown_does_not_fall_back_to_pypi()
        => Assert.False(NexusEcosystems.TryFromFormat("somethingelse", out _));

    [Fact]
    public void FromFormat_refuses_ambiguous_apt_so_debian_ubuntu_are_never_guessed()
        => Assert.False(NexusEcosystems.TryFromFormat("apt", out _)); // both Debian + Ubuntu — must use repo name

    [Theory]
    [InlineData(Ecosystem.Maven, "nexus-osv")]
    [InlineData(Ecosystem.Debian, "nexus-osv")]
    [InlineData(Ecosystem.HuggingFace, "scanner")]
    [InlineData(Ecosystem.Docker, "scanner")]
    [InlineData(Ecosystem.AIEditorExtensions, "scanner")]
    [InlineData(Ecosystem.Conda, "research-only")]
    public void GateMechanism_labels_each_ecosystem_honestly(Ecosystem eco, string expected)
        => Assert.Equal(expected, NexusEcosystems.GateMechanism(eco));

    [Fact]
    public void Gateable_set_excludes_research_only_and_non_nexus_ecosystems()
    {
        var gateable = NexusEcosystems.Gateable.ToHashSet();
        // OSV-covered package ecosystems are gateable via Nexus.
        Assert.Contains(Ecosystem.Maven, gateable);
        Assert.Contains(Ecosystem.RubyGems, gateable);
        Assert.Contains(Ecosystem.Debian, gateable);
        // Conda is deferred (no CVE source); HuggingFace/Docker/extensions use their own scanners.
        Assert.DoesNotContain(Ecosystem.Conda, gateable);
        Assert.DoesNotContain(Ecosystem.HuggingFace, gateable);
        Assert.DoesNotContain(Ecosystem.AIEditorExtensions, gateable);
    }
}
