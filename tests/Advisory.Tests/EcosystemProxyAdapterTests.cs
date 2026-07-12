using Advisory.Api.Models;
using Advisory.Api.Proxy;
using Advisory.Api.Proxy.Adapters;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Unit tests for the reverse-proxy ecosystem adapters. These assert each adapter parses coordinates and
/// maps URLs for its REAL protocol path shapes (the ones confirmed live against the Nexus proxies), so a
/// wrong format is caught here rather than silently failing an install. Covers PyPI, npm, NuGet, Go.
/// </summary>
public class EcosystemProxyAdapterTests
{
    private const string Nexus = "http://nexus:8081";

    // ─────────────── PyPI ───────────────
    [Fact]
    public void PyPi_parses_wheel_path()
    {
        var a = new PyPiProxyAdapter();
        var (name, version, file) = a.ParseArtifactPath("packages/six/1.16.0/six-1.16.0-py2.py3-none-any.whl");
        Assert.Equal("six", name);
        Assert.Equal("1.16.0", version);
        Assert.Equal("six-1.16.0-py2.py3-none-any.whl", file);
    }

    [Fact]
    public void PyPi_maps_index_and_treats_metadata_as_ungated()
    {
        var a = new PyPiProxyAdapter();
        var idx = a.MapIndexRequest("simple/requests", Nexus);
        Assert.NotNull(idx);
        Assert.Contains("pypi-quarantine/simple/requests/", idx!.Value.upstreamUrl);
        Assert.True(a.IsUngatedMetadata("packages/x/1/x-1.whl.metadata"));
    }

    // ─────────────── npm ───────────────
    [Theory]
    [InlineData("lodash/-/lodash-4.17.21.tgz", "lodash", "4.17.21")]
    [InlineData("@babel/core/-/core-7.24.0.tgz", "@babel/core", "7.24.0")]
    public void Npm_parses_tarball_path(string rest, string expectName, string expectVer)
    {
        var a = new NpmProxyAdapter();
        var (name, version, file) = a.ParseArtifactPath(rest);
        Assert.Equal(expectName, name);
        Assert.Equal(expectVer, version);
        Assert.EndsWith(".tgz", file);
    }

    [Fact]
    public void Npm_rewrites_tarball_urls_to_proxy()
    {
        var a = new NpmProxyAdapter();
        var doc = "{\"dist\":{\"tarball\":\"http://nexus:8081/repository/npm-quarantine/lodash/-/lodash-4.17.21.tgz\"}}";
        var rw = a.RewriteIndex(doc, Nexus);
        Assert.Contains("/npm/artifact/lodash/-/lodash-4.17.21.tgz", rw);
        Assert.DoesNotContain("npm-quarantine", rw);
    }

    [Fact]
    public void Npm_package_document_is_an_index_not_an_artifact()
    {
        var a = new NpmProxyAdapter();
        Assert.NotNull(a.MapIndexRequest("lodash", Nexus));        // package doc → index
        Assert.Null(a.MapIndexRequest("lodash/-/lodash-4.17.21.tgz", Nexus)); // tarball → not index
    }

    // ─────────────── NuGet ───────────────
    [Fact]
    public void NuGet_parses_flatcontainer_nupkg_path()
    {
        var a = new NuGetProxyAdapter();
        var (name, version, file) = a.ParseArtifactPath("v3/content/0/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg");
        Assert.Equal("newtonsoft.json", name);
        Assert.Equal("13.0.3", version);
        Assert.EndsWith(".nupkg", file);
    }

    [Fact]
    public void NuGet_rewrites_service_index_urls()
    {
        var a = new NuGetProxyAdapter();
        var doc = "{\"@id\":\"http://nexus:8081/repository/nuget-quarantine/v3/content/0/\","
                + "\"pkg\":\"http://nexus:8081/repository/nuget-quarantine/v3/content/0/n/1.0.0/n.1.0.0.nupkg\"}";
        var rw = a.RewriteIndex(doc, Nexus);
        Assert.Contains("/nuget/artifact/v3/content/0/n/1.0.0/n.1.0.0.nupkg", rw);  // nupkg → artifact route
        Assert.Contains("/nuget/index/v3/content/0/", rw);                          // other → index route
        Assert.DoesNotContain("nuget-quarantine", rw);
    }

    // ─────────────── Go ───────────────
    [Fact]
    public void Go_parses_module_zip_path_with_slashes()
    {
        var a = new GoProxyAdapter();
        var (name, version, file) = a.ParseArtifactPath("rsc.io/quote/@v/v1.5.2.zip");
        Assert.Equal("rsc.io/quote", name);
        Assert.Equal("v1.5.2", version);
        Assert.Equal("v1.5.2.zip", file);
    }

    [Fact]
    public void Go_list_info_mod_are_ungated_metadata_zip_is_gated()
    {
        var a = new GoProxyAdapter();
        Assert.True(a.IsUngatedMetadata("rsc.io/quote/@v/list"));
        Assert.True(a.IsUngatedMetadata("rsc.io/quote/@v/v1.5.2.info"));
        Assert.True(a.IsUngatedMetadata("rsc.io/quote/@v/v1.5.2.mod"));
        Assert.False(a.IsUngatedMetadata("rsc.io/quote/@v/v1.5.2.zip"));
    }
}
