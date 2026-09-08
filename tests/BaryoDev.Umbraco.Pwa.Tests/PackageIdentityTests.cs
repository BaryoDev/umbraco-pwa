using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The backoffice manifest and the csproj describe the same package to two different audiences,
/// and nothing in the build makes them agree. Umbraco reports install telemetry under the id in
/// the manifest, while the Marketplace listing is keyed on the NuGet PackageId, so a disagreement
/// is not a broken build. It is installs counted against a name the listing does not use, which
/// is invisible until someone wonders why a published package shows no adoption.
/// </summary>
[Collection(UmbracoCollection.Name)]
public class PackageIdentityTests
{
    private readonly UmbracoSiteFixture _site;

    public PackageIdentityTests(UmbracoSiteFixture site) => _site = site;

    [Fact]
    public async Task The_manifest_id_is_the_NuGet_package_id()
    {
        // Telemetry falls back to the manifest name, then the App_Plugins folder name, and both
        // of those are "BaryoDev.Pwa" here. Only the id can carry the listing's key.
        (await ManifestString("id")).ShouldBe(ProductProperty("PackageId"));
    }

    [Fact]
    public async Task The_manifest_version_is_the_package_version()
    {
        // This drifted once already and shipped four releases saying 0.1.0.
        (await ManifestString("version")).ShouldBe(ProductProperty("Version"));
    }

    private async Task<string?> ManifestString(string property)
    {
        // Over HTTP, not off disk: static web assets from a Razor Class Library are served
        // through a manifest rather than copied, so this is the copy the backoffice actually
        // reads and the only one telemetry could ever see.
        var manifest = await _site.Client.GetStringAsync(
            "/App_Plugins/BaryoDev.Pwa/umbraco-package.json");

        using var doc = JsonDocument.Parse(manifest);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }

    private static string ProductProperty(string name)
    {
        // Reading the csproj rather than restating its values, or this would only prove that two
        // literals in the test repository agree with each other.
        var path = typeof(PackageIdentityTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ProductProjectFile").Value!;

        var value = XDocument.Load(path).Descendants(name).SingleOrDefault()?.Value;

        return value.ShouldNotBeNull($"{name} must be set in {Path.GetFileName(path)}");
    }
}
