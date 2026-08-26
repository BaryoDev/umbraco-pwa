using System.Reflection;
using Umbraco.Cms.Core;

namespace BaryoDev.Umbraco.Pwa.Tests;

internal class TestProxy : DispatchProxy
{
    public List<string> Calls { get; } = [];
    public RuntimeLevel RuntimeLevel { get; set; } = RuntimeLevel.Run;
    public string? KeyValue { get; set; }
    public bool ThrowOnInvocation { get; set; }

    public static T Create<T>()
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        return proxy;
    }

    public static TestProxy For<T>(T proxy) => (TestProxy)(object)proxy!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod?.Name ?? string.Empty);

        if (ThrowOnInvocation)
        {
            throw new InvalidOperationException($"Unexpected call to {targetMethod?.Name}.");
        }

        return targetMethod?.Name switch
        {
            "get_Level" => RuntimeLevel,
            "GetValue" => KeyValue,
            "SetValue" => SetKeyValue(args),
            _ => targetMethod is null || targetMethod.ReturnType == typeof(void)
                ? null
                : targetMethod.ReturnType.IsValueType
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null,
        };
    }

    private object? SetKeyValue(object?[]? args)
    {
        if (args is { Length: > 1 })
        {
            KeyValue = args[1]?.ToString();
        }

        return null;
    }
}
