using System.Reflection;
using PublicApiGenerator;
using Xunit;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.ApiApproval.Tests;

/// <summary>
/// Pins the public surface of the package.
/// </summary>
/// <remarks>
/// Other people's sites compile against this. Renaming an export, changing an arity or dropping a
/// member breaks them even when every behavioural test still passes, and nothing else in the suite
/// would notice. Rendering the surface to text and diffing it turns that into a failing test and a
/// reviewable diff.
///
/// A failure here is not necessarily wrong. Read the diff, decide whether the change is additive
/// or breaking, then approve it by copying the .received.txt over the .approved.txt and committing
/// both, along with the version bump it implies: additions are a minor, anything removed or
/// changed in place is a major.
/// </remarks>
public class PublicApiTests
{
    private static readonly string ApprovedDirectory =
        Path.Combine(AppContext.BaseDirectory, "approved-api");

    [Fact]
    public void The_public_api_has_not_changed()
    {
        var assembly = typeof(PwaOptions).Assembly;

        var actual = assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            // Attributes churn with build tooling rather than with the API, and a snapshot that
            // churns is one people approve without reading.
            ExcludeAttributes =
            [
                "System.Runtime.Versioning.TargetFrameworkAttribute",
                "System.Reflection.AssemblyMetadataAttribute",
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
            ],
        }).Trim();

        Directory.CreateDirectory(ApprovedDirectory);

        var approvedPath = Path.Combine(ApprovedDirectory, "BaryoDev.Umbraco.Pwa.approved.txt");
        var receivedPath = Path.Combine(ApprovedDirectory, "BaryoDev.Umbraco.Pwa.received.txt");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(receivedPath, actual);
            throw new Xunit.Sdk.XunitException(
                $"No approved API file. Review {receivedPath} and, if it is what you meant to " +
                "publish, rename it to BaryoDev.Umbraco.Pwa.approved.txt.");
        }

        var approved = File.ReadAllText(approvedPath).Trim();

        if (approved == actual)
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);

        var before = approved.Split('\n').Select(l => l.TrimEnd()).ToList();
        var after = actual.Split('\n').Select(l => l.TrimEnd()).ToList();
        var removed = before.Except(after).ToList();
        var added = after.Except(before).ToList();

        var report = new List<string>();
        if (removed.Count > 0)
            report.Add($"Removed or changed ({removed.Count}):\n  - " + string.Join("\n  - ", removed));
        if (added.Count > 0)
            report.Add($"Added ({added.Count}):\n  + " + string.Join("\n  + ", added));

        throw new Xunit.Sdk.XunitException(
            $"The public API changed.\n\n{string.Join("\n\n", report)}\n\n" +
            $"If that is intended, copy\n  {receivedPath}\nover\n  {approvedPath}\n" +
            "and make sure the version bump matches: additions are a minor, anything removed or " +
            "changed in place is a major.");
    }

    [Fact]
    public void Nothing_internal_leaked_into_the_public_surface()
    {
        // The services are registered by the composer and resolved by the framework, so they have
        // no reason to be public. Making one public by accident is a maintenance commitment
        // nobody meant to sign up for.
        var assembly = typeof(PwaOptions).Assembly;

        var publicTypes = assembly.GetExportedTypes()
            .Where(t => t.Namespace?.Contains(".Services") == true && !t.IsInterface)
            .Select(t => t.Name)
            .ToList();

        publicTypes.ShouldBeEmpty(
            "service implementations should stay internal; only their interfaces are public");
    }
}
