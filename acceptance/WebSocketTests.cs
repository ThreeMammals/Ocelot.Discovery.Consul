using Consul;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Ocelot.LoadBalancer.Balancers;

namespace Ocelot.Discovery.Consul.Acceptance;

public sealed class WebSocketTests : ConsulWebSocketsSteps
{
    private readonly List<ServiceEntry> _serviceEntries = [];

    [Fact]
    [Trait("Feat", "212")] // https://github.com/ThreeMammals/Ocelot/issues/212
    [Trait("PR", "273")] // https://github.com/ThreeMammals/Ocelot/pull/273
    [Trait("Release", "5.3.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/5.3.0
    [Trait("Commit", "463a7bd")] // https://github.com/ThreeMammals/Ocelot/commit/463a7bdab4652762d14779e7e3f62a207c3d421c
    public async Task Should_proxy_websocket_input_to_downstream_service_and_use_service_discovery_and_load_balancer()
    {
        var downstreamPort = PortFinder.GetRandomPort();
        var downstreamHost = "localhost";

        var secondDownstreamPort = PortFinder.GetRandomPort();
        var secondDownstreamHost = "localhost";

        const string ServiceName = "websockets";
        var consulPort = PortFinder.GetRandomPort();
        var serviceEntryOne = new ServiceEntry
        {
            Service = new AgentService
            {
                Service = ServiceName,
                Address = downstreamHost,
                Port = downstreamPort,
                ID = Guid.NewGuid().ToString(),
                Tags = [],
            },
        };
        var serviceEntryTwo = new ServiceEntry
        {
            Service = new AgentService
            {
                Service = ServiceName,
                Address = secondDownstreamHost,
                Port = secondDownstreamPort,
                ID = Guid.NewGuid().ToString(),
                Tags = [],
            },
        };
        var route = GivenRoute(0, "/", "/ws");
        route.DownstreamHostAndPorts.Clear();
        route.DownstreamScheme = Uri.UriSchemeWs;
        route.LoadBalancerOptions = new(nameof(RoundRobin));
        route.ServiceName = ServiceName;
        var config = GivenDiscoveryConfiguration([route], consulPort);
        int ocelotPort = PortFinder.GetRandomPort();
        GivenThereIsAConfiguration(config);
        await StartOcelotWithWebSockets(ocelotPort, WithConsul);
        GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort, ServiceName);
        GivenTheServicesAreRegisteredWithConsul(serviceEntryOne, serviceEntryTwo);
        await GivenWebSocketsServiceIsRunningAsync(downstreamPort, "/ws", EchoAsync);
        await GivenWebSocketsServiceIsRunningAsync(secondDownstreamPort, "/ws", MessageAsync);
        await WhenIStartTheClients(ocelotPort);
        ThenBothDownstreamServicesAreCalled();
    }

    private void GivenTheServicesAreRegisteredWithConsul(params ServiceEntry[] serviceEntries)
    {
        foreach (var serviceEntry in serviceEntries)
        {
            _serviceEntries.Add(serviceEntry);
        }
    }

    private void GivenThereIsAFakeConsulServiceDiscoveryProvider(int port, string serviceName)
    {
        Task MapServicePath(HttpContext context)
        {
            if (context.Request.Path.Value == $"/v1/health/service/{serviceName}")
            {
                var json = JsonConvert.SerializeObject(_serviceEntries);
                context.Response.Headers.Append("Content-Type", "application/json");
                return context.Response.WriteAsync(json, context.RequestAborted);
            }
            return Task.CompletedTask;
        }
        handler.GivenThereIsAServiceRunningOn(port, MapServicePath);
    }
}
