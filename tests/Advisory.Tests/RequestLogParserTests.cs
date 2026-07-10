using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the Nexus request.log parser used by auto-gate-on-pull: a 404 line for a not-yet-approved
/// package must yield the ecosystem + package name so the tailer can enqueue it. Lines that aren't
/// a package miss (200s, non-repository paths, other status codes) must yield nothing. Sample lines
/// are the real Apache-style format verified against Nexus 3.93 Community.
/// </summary>
public class RequestLogParserTests
{
    // The exact line shape captured from a live Nexus 3.93 CE request.log for a pip miss.
    const string PipMiss =
        "192.168.80.1 - - [10/Jul/2026:07:14:40 +0000] \"GET /repository/pypi-approved/simple/this-pkg-does-not-exist-xyz123/ HTTP/1.1\" 404 - 1381 1199 \"curl/8.12.1\" [qtp1199213870-40]";

    [Fact]
    public void Parses_pip_simple_index_404_into_pypi_package()
    {
        Assert.True(RequestLogParser.TryParseMiss(PipMiss, out var pkg));
        Assert.Equal(Ecosystem.PyPI, pkg!.Ecosystem);
        Assert.Equal("this-pkg-does-not-exist-xyz123", pkg.Name);
    }

    [Fact]
    public void Parses_pip_wheel_download_404_to_the_same_package_name()
    {
        // pip requests the wheel after the index; a miss there must map to the SAME package name so
        // the tailer's dedup collapses index + artifact requests to one enqueue.
        const string wheel =
            "10.0.0.5 - - [10/Jul/2026:07:15:01 +0000] \"GET /repository/pypi-approved/packages/requests/2.31.0/requests-2.31.0-py3-none-any.whl HTTP/1.1\" 404 - 0 3 \"pip/24.0\" [qtp-1]";
        Assert.True(RequestLogParser.TryParseMiss(wheel, out var pkg));
        Assert.Equal(Ecosystem.PyPI, pkg!.Ecosystem);
        Assert.Equal("requests", pkg.Name);
    }

    [Fact]
    public void Parses_npm_metadata_404_into_npm_package()
    {
        const string meta =
            "10.0.0.5 - - [10/Jul/2026:07:15:01 +0000] \"GET /repository/npm-approved/lodash HTTP/1.1\" 404 - 0 3 \"npm/10.5.0 node/v20\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(meta, out var pkg));
        Assert.Equal(Ecosystem.npm, pkg!.Ecosystem);
        Assert.Equal("lodash", pkg.Name);
    }

    [Fact]
    public void Parses_npm_tarball_404_to_the_package_name()
    {
        const string tgz =
            "10.0.0.5 - - [10/Jul/2026:07:15:02 +0000] \"GET /repository/npm-approved/lodash/-/lodash-4.17.21.tgz HTTP/1.1\" 404 - 0 3 \"npm/10\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(tgz, out var pkg));
        Assert.Equal(Ecosystem.npm, pkg!.Ecosystem);
        Assert.Equal("lodash", pkg.Name);
    }

    [Fact]
    public void Parses_npm_scoped_metadata_url_encoded_slash()
    {
        // npm requests scoped-package metadata with the slash URL-encoded: @org%2fpkg
        const string scoped =
            "10.0.0.5 - - [10/Jul/2026:07:15:03 +0000] \"GET /repository/npm-approved/@babel%2fcore HTTP/1.1\" 404 - 0 3 \"npm/10\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(scoped, out var pkg));
        Assert.Equal(Ecosystem.npm, pkg!.Ecosystem);
        Assert.Equal("@babel/core", pkg.Name);
    }

    [Fact]
    public void Parses_npm_scoped_tarball()
    {
        // scoped tarball: /@org/pkg/-/pkg-1.2.3.tgz  (slash NOT encoded in the tarball path form)
        const string scopedTgz =
            "10.0.0.5 - - [10/Jul/2026:07:15:04 +0000] \"GET /repository/npm-approved/@babel/core/-/core-7.24.0.tgz HTTP/1.1\" 404 - 0 3 \"npm/10\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(scopedTgz, out var pkg));
        Assert.Equal(Ecosystem.npm, pkg!.Ecosystem);
        Assert.Equal("@babel/core", pkg.Name);
    }

    [Fact]
    public void Ignores_non_404_lines()
    {
        const string ok =
            "192.168.80.1 - admin [10/Jul/2026:07:14:09 +0000] \"GET /repository/pypi-approved/simple/six/ HTTP/1.1\" 200 - 49 6 \"pip/24.0\" [qtp-1]";
        Assert.False(RequestLogParser.TryParseMiss(ok, out _));
    }

    [Fact]
    public void Ignores_non_repository_paths()
    {
        const string rest =
            "192.168.80.6 - admin [10/Jul/2026:07:14:09 +0000] \"GET /service/rest/v1/components?repository=conan-quarantine HTTP/1.1\" 404 - 49 6 \"\" [qtp-1]";
        Assert.False(RequestLogParser.TryParseMiss(rest, out _));
    }

    [Fact]
    public void Ignores_quarantine_repo_requests_only_gates_from_approved()
    {
        // Only developer-facing approved-repo misses trigger gating; internal quarantine reads must not.
        const string quar =
            "192.168.80.6 - admin [10/Jul/2026:07:14:09 +0000] \"GET /repository/pypi-quarantine/simple/foo/ HTTP/1.1\" 404 - 49 6 \"\" [qtp-1]";
        Assert.False(RequestLogParser.TryParseMiss(quar, out _));
    }

    [Fact]
    public void Ignores_garbage_lines()
    {
        Assert.False(RequestLogParser.TryParseMiss("", out _));
        Assert.False(RequestLogParser.TryParseMiss("not a log line at all", out _));
    }
}
