using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins PyPI pre-release detection: auto-gate-on-pull must pick the newest STABLE version (what
/// `pip install <name>` resolves to), not a newer pre-release, so the developer's retry gets the
/// version pip actually wants.
/// </summary>
public class PyPreReleaseTests
{
    [Theory]
    [InlineData("wrapt-2.3.0rc1.tar.gz", true)]
    [InlineData("foo-1.0b2-py3-none-any.whl", true)]
    [InlineData("bar-2.0.dev3-py3-none-any.whl", true)]
    [InlineData("baz-1.0a1.tar.gz", true)]
    [InlineData("pkg-3.0.0alpha1.tar.gz", true)]
    // Stable releases — must NOT be flagged:
    [InlineData("wrapt-1.16.0-cp312-cp312-win_amd64.whl", false)]
    [InlineData("six-1.16.0-py2.py3-none-any.whl", false)]
    [InlineData("requests-2.31.0.tar.gz", false)]
    [InlineData("numpy-1.26.4-cp312-cp312-win_amd64.whl", false)]  // 'cp312' must not trip it
    public void Detects_pypi_prereleases(string fileName, bool expected)
        => Assert.Equal(expected, NexusClient.IsPyPreRelease(fileName));

    [Theory]
    [InlineData("idna-3.18-py3-none-any.whl", "idna", "3.18")]
    [InlineData("idna-3.18.tar.gz", "idna", "3.18")]
    [InlineData("charset_normalizer-3.3.2-cp312-cp312-win_amd64.whl", "charset_normalizer", "3.3.2")]
    [InlineData("requests-2.31.0.tar.gz", "requests", "2.31.0")]
    public void Extracts_pypi_version(string fileName, string name, string expected)
        => Assert.Equal(expected, NexusClient.PyVersionOf(fileName, name));
}
