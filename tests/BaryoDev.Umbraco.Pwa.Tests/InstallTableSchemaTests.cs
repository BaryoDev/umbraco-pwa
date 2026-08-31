using BaryoDev.Umbraco.Pwa.Persistence;
using Shouldly;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The shape of the install table, pinned.
/// </summary>
/// <remarks>
/// The database is one of this package's public surfaces and it is the only one with no gate. The
/// assembly API has an approval test, and the generated assets have browser tests, but the table
/// lives in the site owner's own Umbraco database and a change to it is not something they can
/// undo by pinning a version.
///
/// The migration plan currently has one step, which creates the table and skips when it already
/// exists. That makes an upgrade trivially safe today and dangerous tomorrow: adding a property to
/// <see cref="PwaInstallDto"/> changes what a fresh install creates and does nothing at all to a
/// site that already ran the first step. The two would silently diverge, and the symptom would be
/// a missing column on exactly the sites that have been running longest.
///
/// This is the test that turns that into a build failure. If it fails because a column was added
/// deliberately, the fix is a new migration step chained after the previous state, then update the
/// expectation here.
/// </remarks>
[Collection(UmbracoCollection.Name)]
public class InstallTableSchemaTests
{
    private readonly UmbracoSiteFixture _site;

    public InstallTableSchemaTests(UmbracoSiteFixture site) => _site = site;

    /// <summary>Column name to SQLite declared type, as the table exists right now.</summary>
    private static readonly (string Name, string Type, bool NotNull)[] Expected =
    [
        ("id", "INTEGER", true),
        ("deviceId", "TEXT", true),
        ("platform", "TEXT", false),
        ("displayMode", "TEXT", true),
        ("installed", "INTEGER", true),
        ("firstSeenAt", "TEXT", true),
        ("lastSeenAt", "TEXT", true),
        ("installedAt", "TEXT", false),
        ("launchCount", "INTEGER", true),
    ];

    private List<(string Name, string Type, bool NotNull)> ActualColumns()
    {
        using var scope = _site.Resolve<IScopeProvider>().CreateScope(autoComplete: true);

        // pragma_table_info rather than PRAGMA, because NPoco wraps a statement that does not
        // start with SELECT in one of its own and the result is a syntax error.
        return scope.Database
            .Fetch<dynamic>(
                "SELECT name, type, \"notnull\" AS isnotnull FROM pragma_table_info(@0) ORDER BY cid",
                PwaInstallDto.TableName)
            .Select(r => (
                Name: (string)r.name,
                Type: ((string)r.type).ToUpperInvariant(),
                NotNull: (bool)(Convert.ToInt64((object)r.isnotnull) == 1)))
            .ToList();
    }

    [Fact]
    public void The_table_has_exactly_the_columns_the_dto_declares()
    {
        var actual = ActualColumns();

        // Names and order first, so a diff reads as one line rather than nine.
        actual.Select(c => c.Name).ShouldBe(Expected.Select(e => e.Name),
            "a column added to PwaInstallDto without a migration step reaches a fresh install and "
            + "never reaches an upgraded one");
    }

    [Fact]
    public void Every_column_keeps_its_type_and_nullability()
    {
        // Separate from the names, because a type or nullability change is a different mistake with
        // a different fix, and one assertion covering both says less about which happened.
        ActualColumns().ShouldBe(Expected.ToList());
    }

    [Fact]
    public void The_device_id_is_unique_and_last_seen_is_indexed()
    {
        // The unique index is what makes concurrent first reports for one device collapse into a
        // single row rather than racing into duplicates, and the lastSeenAt index is what keeps
        // retention from scanning the table. Both are behaviour, not decoration.
        using var scope = _site.Resolve<IScopeProvider>().CreateScope(autoComplete: true);

        var indexes = scope.Database
            .Fetch<dynamic>(
                "SELECT name, \"unique\" AS isunique FROM pragma_index_list(@0)",
                PwaInstallDto.TableName)
            .Select(r => (
                Name: (string)r.name,
                Unique: (bool)(Convert.ToInt64((object)r.isunique) == 1)))
            .ToList();

        indexes.ShouldContain(("IX_BaryoDevPwaInstall_deviceId", true));
        indexes.ShouldContain(("IX_BaryoDevPwaInstall_lastSeenAt", false));
    }

    [Fact]
    public void Nothing_in_the_table_can_identify_a_visitor()
    {
        // SECURITY.md promises this and it is checkable against the schema rather than against
        // anyone's intent. A column added later that carries an address, an agent or a name would
        // break the promise the package is sold on, and this is the only thing that would notice.
        var forbidden = new[] { "ip", "address", "useragent", "agent", "email", "name", "user", "location", "country" };

        foreach (var column in ActualColumns().Select(c => c.Name.ToLowerInvariant()))
        {
            // deviceId is the browser-generated id and is the one identifier that is allowed.
            if (column == "deviceid") continue;

            forbidden.ShouldNotContain(
                f => column.Contains(f, StringComparison.Ordinal),
                $"the column '{column}' looks like it identifies a visitor, which SECURITY.md says nothing here does");
        }
    }
}
