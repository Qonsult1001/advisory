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
    public void Parses_nuget_flat_container_404()
    {
        const string nuget =
            "10.0.0.5 - - [10/Jul/2026:07:15:05 +0000] \"GET /repository/nuget-approved/v3/content/0/newtonsoft.json/index.json HTTP/1.1\" 404 - 0 3 \"NuGet\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(nuget, out var pkg));
        Assert.Equal(Ecosystem.NuGet, pkg!.Ecosystem);
        Assert.Equal("newtonsoft.json", pkg.Name);
    }

    [Fact]
    public void Parses_nuget_nupkg_download_404()
    {
        const string nupkg =
            "10.0.0.5 - - [10/Jul/2026:07:15:06 +0000] \"GET /repository/nuget-approved/v3/content/0/serilog/3.1.1/serilog.3.1.1.nupkg HTTP/1.1\" 404 - 0 3 \"NuGet\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(nupkg, out var pkg));
        Assert.Equal(Ecosystem.NuGet, pkg!.Ecosystem);
        Assert.Equal("serilog", pkg.Name);
    }

    [Fact]
    public void Parses_cargo_crate_download_404()
    {
        const string cargo =
            "10.0.0.5 - - [10/Jul/2026:07:15:07 +0000] \"GET /repository/cargo-approved/api/v1/crates/serde/1.0.197/download HTTP/1.1\" 404 - 0 3 \"cargo\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(cargo, out var pkg));
        Assert.Equal(Ecosystem.Cargo, pkg!.Ecosystem);
        Assert.Equal("serde", pkg.Name);
    }

    [Fact]
    public void Parses_cargo_metadata_404()
    {
        const string cargo =
            "10.0.0.5 - - [10/Jul/2026:07:15:08 +0000] \"GET /repository/cargo-approved/api/v1/crates/tokio HTTP/1.1\" 404 - 0 3 \"cargo\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(cargo, out var pkg));
        Assert.Equal(Ecosystem.Cargo, pkg!.Ecosystem);
        Assert.Equal("tokio", pkg.Name);
    }

    [Fact]
    public void Parses_go_module_proxy_404_with_slashed_module_path()
    {
        // Go module path contains slashes; the name is everything before /@v/.
        const string go =
            "10.0.0.5 - - [10/Jul/2026:07:15:09 +0000] \"GET /repository/go-approved/github.com/gorilla/mux/@v/v1.8.1.info HTTP/1.1\" 404 - 0 3 \"Go-http-client\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(go, out var pkg));
        Assert.Equal(Ecosystem.Go, pkg!.Ecosystem);
        Assert.Equal("github.com/gorilla/mux", pkg.Name);
    }

    [Fact]
    public void Parses_go_at_latest_404()
    {
        const string go =
            "10.0.0.5 - - [10/Jul/2026:07:15:10 +0000] \"GET /repository/go-approved/golang.org/x/text/@latest HTTP/1.1\" 404 - 0 3 \"Go-http-client\" [q]";
        Assert.True(RequestLogParser.TryParseMiss(go, out var pkg));
        Assert.Equal(Ecosystem.Go, pkg!.Ecosystem);
        Assert.Equal("golang.org/x/text", pkg.Name);
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
