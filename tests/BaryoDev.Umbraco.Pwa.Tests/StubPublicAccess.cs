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

    public Attempt<PublicAccessEntry?> IsProtected(string path) =>
        protectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            ? Attempt<PublicAccessEntry?>.Succeed(null)
            : Attempt<PublicAccessEntry?>.Fail();

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
