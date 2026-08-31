using System.Net;
using BaryoDev.Umbraco.Pwa.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
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
    private static PwaReportRateLimitFilter Filter(int perMinute) =>
        new(new StaticOptionsMonitor(new PwaOptions { MaxReportsPerMinute = perMinute }));

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
