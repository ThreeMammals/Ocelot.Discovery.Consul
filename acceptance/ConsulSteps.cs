using Microsoft.Extensions.DependencyInjection;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.Testing.Steps;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ocelot.Discovery.Consul.Acceptance;

public class ConsulSteps : DiscoverySteps
{
    public static readonly FileRoute[] NoRoutes = null;

    public FileConfiguration GivenDiscoveryConfiguration(FileRoute[] routes, int port,
        string scheme = null, string host = null, string provider = null)
    {
        routes ??= [];
        var c = GivenConfiguration(routes);
        c.GlobalConfiguration.ServiceDiscoveryProvider = new()
        {
            Scheme = scheme ?? Uri.UriSchemeHttp,
            Host = host ?? "localhost",
            Port = port,
            Type = provider ?? nameof(Consul),
        };
        return c;
    }

    public static void WithConsul(IServiceCollection services)
        => services.AddOcelot().AddConsul();
}

public class ConsulRateLimitingSteps : RateLimitingSteps
{
    private readonly ConsulSteps steps = new();
    public override void Dispose()
    {
        steps.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public FileConfiguration GivenDiscoveryConfiguration(FileRoute[] routes, int port, string scheme = null, string host = null, string provider = null)
        => steps.GivenDiscoveryConfiguration(routes, port, scheme, host, provider);

    public MethodInfo Method(string name)
        => steps.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod);

    public string ServiceName([CallerMemberName] string serviceName = null)
        => Method(nameof(ServiceName)).Invoke(steps, [serviceName]) as string;

    public string ServiceNamespace()
        => GetType().Namespace;
}

public class ConsulWebSocketsSteps : WebSocketsSteps
{
    private readonly ConsulSteps steps = new();
    public override void Dispose()
    {
        steps.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public static void WithConsul(IServiceCollection services)
        => ConsulSteps.WithConsul(services);

    public FileConfiguration GivenDiscoveryConfiguration(FileRoute[] routes, int port, string scheme = null, string host = null, string provider = null)
        => steps.GivenDiscoveryConfiguration(routes, port, scheme, host, provider);
}
