using System.Net;
using BaryoDev.Umbraco.Pwa.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Tests;

/// <summary>
/// The bound on how many rows one anonymous caller can have stored.
/// </summary>
/// <remarks>
/// Driven through the filter rather than over HTTP. The shipped default is 120 a minute, so an
/// end-to-end test would need 121 requests to prove anything, and the interesting cases are the
/// partition key and the response given when it declines, both of which are clearer here.
/// </remarks>
public class ReportRateLimitTests
{
    private static PwaReportRateLimitFilter Filter(int perMinute, ILogger<PwaReportRateLimitFilter>? logger = null) =>
        new(
            new StaticOptionsMonitor(new PwaOptions { MaxReportsPerMinute = perMinute }),
            logger ?? NullLogger<PwaReportRateLimitFilter>.Instance);

    private sealed class CapturingLogger : ILogger<PwaReportRateLimitFilter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static ActionExecutingContext Context(string? callerIp)
    {
        var http = new DefaultHttpContext();
        if (callerIp is not null) http.Connection.RemoteIpAddress = IPAddress.Parse(callerIp);

        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ControllerActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: null!);
    }

    /// <summary>Runs the filter once and says whether the action underneath was reached.</summary>
    private static async Task<(bool Reached, IActionResult? Result)> Run(
        PwaReportRateLimitFilter filter,
        string? callerIp)
    {
        var context = Context(callerIp);
        var reached = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: null!));
        });

        return (reached, context.Result);
    }

    [Fact]
    public async Task A_caller_over_the_limit_stops_reaching_the_action()
    {
        using var filter = Filter(perMinute: 3);

        var outcomes = new List<bool>();
        for (var i = 0; i < 5; i++) outcomes.Add((await Run(filter, "203.0.113.7")).Reached);

        outcomes.ShouldBe([true, true, true, false, false]);
    }

    [Fact]
    public async Task A_dropped_report_is_logged_once_and_carries_no_address()
    {
        var logger = new CapturingLogger();
        using var filter = Filter(perMinute: 1, logger);

        await Run(filter, "203.0.113.20");
        logger.Entries.ShouldBeEmpty();

        for (var i = 0; i < 5; i++) await Run(filter, "203.0.113.20");

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldNotContain("203.0.113.20");
    }

    [Fact]
    public async Task Declining_still_answers_202()
    {
        // The endpoint has one answer for everything on purpose. A 429 would tell an anonymous
        // caller exactly where the limit is and when it resets, and it would make this endpoint
        // the one place the package says something different depending on what it was sent.
        using var filter = Filter(perMinute: 1);

        await Run(filter, "203.0.113.8");
        var (reached, result) = await Run(filter, "203.0.113.8");

        reached.ShouldBeFalse();
        result.ShouldBeOfType<AcceptedResult>()
            .StatusCode.ShouldBe(StatusCodes.Status202Accepted);
    }

    [Fact]
    public async Task One_caller_burning_its_budget_does_not_spend_anyone_elses()
    {
        // The control that matters. A limiter with one global bucket would pass the first test
        // here and take the whole site down the first time a single client misbehaved.
        using var filter = Filter(perMinute: 2);

        await Run(filter, "203.0.113.9");
        await Run(filter, "203.0.113.9");
        (await Run(filter, "203.0.113.9")).Reached.ShouldBeFalse();

        (await Run(filter, "198.51.100.4")).Reached.ShouldBeTrue();
        (await Run(filter, "198.51.100.4")).Reached.ShouldBeTrue();
    }

    [Fact]
    public async Task Addresses_in_one_ipv6_64_share_a_budget()
    {
        // One subscriber is handed a whole /64 and can use any address in it. Per-address keys
        // would give them a fresh budget for each.
        using var filter = Filter(perMinute: 2);

        (await Run(filter, "2001:db8:1:2::1")).Reached.ShouldBeTrue();
        (await Run(filter, "2001:db8:1:2:aaaa:bbbb:cccc:dddd")).Reached.ShouldBeTrue();
        (await Run(filter, "2001:db8:1:2:ffff::9")).Reached.ShouldBeFalse();
    }

    [Fact]
    public async Task A_different_ipv6_64_has_its_own_budget()
    {
        using var filter = Filter(perMinute: 1);

        (await Run(filter, "2001:db8:1:2::1")).Reached.ShouldBeTrue();
        (await Run(filter, "2001:db8:1:2::2")).Reached.ShouldBeFalse();

        (await Run(filter, "2001:db8:1:3::1")).Reached.ShouldBeTrue();
    }

    [Fact]
    public async Task An_ipv4_mapped_ipv6_address_counts_as_the_ipv4_address()
    {
        using var filter = Filter(perMinute: 2);

        (await Run(filter, "203.0.113.50")).Reached.ShouldBeTrue();
        (await Run(filter, "::ffff:203.0.113.50")).Reached.ShouldBeTrue();
        (await Run(filter, "203.0.113.50")).Reached.ShouldBeFalse();

        // A different IPv4 address that happens to be mapped is not swept into the same /64
        // bucket. Every mapped address shares the ::ffff:0:0/96 prefix, so a /64 key would put
        // all of them in one budget.
        (await Run(filter, "::ffff:203.0.113.51")).Reached.ShouldBeTrue();
    }

    [Fact]
    public async Task Ipv4_callers_are_still_keyed_by_the_full_address()
    {
        using var filter = Filter(perMinute: 1);

        (await Run(filter, "203.0.113.60")).Reached.ShouldBeTrue();
        (await Run(filter, "203.0.113.61")).Reached.ShouldBeTrue();
        (await Run(filter, "203.0.113.60")).Reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Zero_turns_the_limit_off()
    {
        using var filter = Filter(perMinute: 0);

        for (var i = 0; i < 50; i++)
        {
            (await Run(filter, "203.0.113.10")).Reached.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task A_caller_with_no_address_is_limited_rather_than_exempt()
    {
        // Some hosting shapes leave RemoteIpAddress null. Falling through to "no key, no limit"
        // would make the guard optional for exactly the callers hardest to attribute.
        using var filter = Filter(perMinute: 2);

        (await Run(filter, null)).Reached.ShouldBeTrue();
        (await Run(filter, null)).Reached.ShouldBeTrue();
        (await Run(filter, null)).Reached.ShouldBeFalse();
    }

    [Fact]
    public void The_shipped_default_is_generous_enough_for_a_shared_egress_address()
    {
        // Genuine traffic is one report per launch per device, but the partition is the caller's
        // address, and every visitor behind one corporate proxy shares it. This is a reminder that
        // lowering the default is a decision about those sites, not a tightening for free.
        new PwaOptions().MaxReportsPerMinute.ShouldBe(120);
    }

    private sealed class StaticOptionsMonitor(PwaOptions value) : IOptionsMonitor<PwaOptions>
    {
        public PwaOptions CurrentValue { get; } = value;
        public PwaOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PwaOptions, string?> listener) => null;
    }
}
