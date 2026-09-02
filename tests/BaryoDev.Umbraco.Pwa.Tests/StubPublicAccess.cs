using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// Umbraco's public access service, stubbed down to the two members anything here calls.
/// </summary>
/// <remarks>
/// Shared, because the middleware and the readiness check ask the same two questions of it and a
/// second copy would be a second place for the answers to drift.
///
/// Callers only ask whether the sequence is empty and whether the attempt succeeded, so the
/// entries themselves never need to be real.
/// </remarks>
internal sealed class StubPublicAccess(params string[] protectedPaths) : IPublicAccessService
{
    private readonly PublicAccessEntry[] _entries =
        protectedPaths.Length == 0 ? [] : new PublicAccessEntry[1];

    public IEnumerable<PublicAccessEntry> GetAll() => _entries;

    /// <summary>
    /// Matches on a comma-separated content path, the way the real service does.
    /// </summary>
    /// <remarks>
    /// Umbraco's <c>PublicAccessService.IsProtected(string)</c> hands the argument to
    /// <c>GetEntryForContent</c>, which runs <c>GetIdsFromPathReversed()</c> on it and expects
    /// <c>-1,1055,1060</c>. It has no prefix matching over URL paths.
    ///
    /// The throw is the point. An earlier version of this stub matched URL prefixes, so the
    /// middleware could pass <c>Request.Path</c>, get a hit here, and never fire in production.
    /// Refusing the argument the real service cannot use keeps that from being green again.
    /// </remarks>
    public Attempt<PublicAccessEntry?> IsProtected(string contentPath)
    {
        if (!contentPath.Contains(','))
        {
            throw new ArgumentException(
                $"IsProtected takes a comma-separated content path such as -1,1055,1060, not '{contentPath}'.",
                nameof(contentPath));
        }

        return protectedPaths.Any(p => contentPath.Split(',').Contains(p))
            ? Attempt<PublicAccessEntry?>.Succeed(null)
            : Attempt<PublicAccessEntry?>.Fail();
    }

    public Attempt<PublicAccessEntry?> IsProtected(IContent content) => throw new NotSupportedException();

    public PublicAccessEntry? GetEntryForContent(IContent content) => throw new NotSupportedException();

    public PublicAccessEntry? GetEntryForContent(string contentPath) => throw new NotSupportedException();

    public Attempt<OperationResult<OperationResultType, PublicAccessEntry>?> AddRule(IContent content, string ruleType, string ruleValue) => throw new NotSupportedException();

    public Attempt<OperationResult?> RemoveRule(IContent content, string ruleType, string ruleValue) => throw new NotSupportedException();

    public Attempt<OperationResult?> Save(PublicAccessEntry entry) => throw new NotSupportedException();

    public Attempt<OperationResult?> Delete(PublicAccessEntry entry) => throw new NotSupportedException();

    public Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> CreateAsync(PublicAccessEntrySlim entry) => throw new NotSupportedException();

    public Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> UpdateAsync(PublicAccessEntrySlim entry) => throw new NotSupportedException();

    public Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> GetEntryByContentKeyAsync(Guid key) => throw new NotSupportedException();

    public Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> GetEntryByContentKeyWithoutAncestorsAsync(Guid key) => throw new NotSupportedException();

    public Task<Attempt<PublicAccessOperationStatus>> DeleteAsync(Guid key) => throw new NotSupportedException();
}
