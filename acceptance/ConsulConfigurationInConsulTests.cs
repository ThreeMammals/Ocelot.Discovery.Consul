using Consul;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Ocelot.Cache;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using System.Net;
using System.Text;
using TestStack.BDDfy;

namespace Ocelot.Discovery.Consul.Acceptance;

/// <summary>
/// Feature: <see href="https://ocelot.readthedocs.io/en/develop/features/servicediscovery.html#configuration-in-kv-store">Configuration in Consul KV store</see>.
/// </summary>
/// <remarks>
/// Initial commit: <see href="https://github.com/ThreeMammals/Ocelot/commit/c3cd181b90fb5d5353b886073b3b7c66c12c6bab">c3cd181</see> on April 16, 2017.<br/>
/// First release: <see href="https://github.com/ThreeMammals/Ocelot/releases/tag/1.4.2">1.4.2</see> on April 16, 2017.<br/>
/// Pull request: <see href="https://github.com/ThreeMammals/Ocelot/pull/85">85</see>.
/// </remarks>
public sealed class ConsulConfigurationInConsulTests : ConsulRateLimitingSteps
{
    private FileConfiguration _consulConfig;
    private readonly List<ServiceEntry> _consulServices = [];

    [Fact]
    [Trait("Feat", "85")] // https://github.com/ThreeMammals/Ocelot/pull/85
    [Trait("Release", "1.4.2")] // https://github.com/ThreeMammals/Ocelot/releases/tag/1.4.2
    [Trait("Commit", "c3cd181")] // https://github.com/ThreeMammals/Ocelot/commit/c3cd181b90fb5d5353b886073b3b7c66c12c6bab
    public void Should_return_response_200_with_simple_url()
    {
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var route = GivenRoute(servicePort);
        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        var serviceName = ServiceName();
        this.Given(x => GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort, serviceName))
            .And(x => x.GivenThereIsAServiceRunningOn(servicePort, route.UpstreamPathTemplate, HttpStatusCode.OK, "Hello from Laura"))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => x.GivenOcelotIsRunningUsingConsulToStoreConfig())
            .When(x => WhenIGetUrlOnTheApiGateway("/"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "154")] // https://github.com/ThreeMammals/Ocelot/issues/154
    [Trait("PR", "157")] // https://github.com/ThreeMammals/Ocelot/pull/157
    [Trait("Release", "2.0.2")] // https://github.com/ThreeMammals/Ocelot/releases/tag/2.0.2
    [Trait("Commit", "6824210")] // https://github.com/ThreeMammals/Ocelot/commit/68242102d8fd3f634167ff1afd92bafa87081279
    public void Should_load_configuration_out_of_consul()
    {
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var configuration = GivenDiscoveryConfiguration([], consulPort); // No routes -> 404 Not Found
        var route = GivenRoute(servicePort, "/cs/status", "/status");
        var consulConfig = GivenDiscoveryConfiguration([route], consulPort);
        var serviceName = ServiceName();
        this.Given(x => GivenTheConsulConfigurationIs(consulConfig))
            .And(x => GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort, serviceName))
            .And(x => x.GivenThereIsAServiceRunningOn(servicePort, "/status", HttpStatusCode.OK, "Hello from Laura"))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => x.GivenOcelotIsRunningUsingConsulToStoreConfig())
            .When(x => WhenIGetUrlOnTheApiGateway("/cs/status"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "154")] // https://github.com/ThreeMammals/Ocelot/issues/154
    [Trait("PR", "157")] // https://github.com/ThreeMammals/Ocelot/pull/157
    [Trait("Release", "2.0.2")] // https://github.com/ThreeMammals/Ocelot/releases/tag/2.0.2
    [Trait("Commit", "6824210")] // https://github.com/ThreeMammals/Ocelot/commit/68242102d8fd3f634167ff1afd92bafa87081279
    public void Should_load_configuration_out_of_consul_if_it_is_changed()
    {
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var route1 = GivenRoute(servicePort, "/cs/status", "/status");
        var configuration = GivenDiscoveryConfiguration([], consulPort); // No routes
        var consulConfig = GivenDiscoveryConfiguration([route1], consulPort);
        var route2 = GivenRoute(servicePort, "/cs/status/awesome", "/status");
        var consulConfig2 = GivenDiscoveryConfiguration([route2], consulPort);
        var serviceName = ServiceName();
        this.Given(x => GivenTheConsulConfigurationIs(consulConfig))
            .And(x => GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort, serviceName))
            .And(x => GivenThereIsAServiceRunningOn(servicePort, "/status", HttpStatusCode.OK, "Hello from Laura"))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunningUsingConsulToStoreConfig())
            .When(x => WhenIGetUrlOnTheApiGateway("/cs/status"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
            .Given(x => GivenTheConsulConfigurationIs(consulConfig2))
            .Then(x => ThenTheConfigIsUpdatedInOcelot("/cs/status/awesome"))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "458")] // https://github.com/ThreeMammals/Ocelot/issues/458
    [Trait("PR", "508")] // https://github.com/ThreeMammals/Ocelot/pull/508
    [Trait("Release", "8.0.4")] // https://github.com/ThreeMammals/Ocelot/releases/tag/8.0.4
    [Trait("Commit", "b0a20d1")] // https://github.com/ThreeMammals/Ocelot/commit/b0a20d13b93acb829ba1c9c6ee25b77564f49fec
    public void Should_handle_request_to_consul_for_downstream_service_and_make_request_no_re_routes_and_rate_limit()
    {
        var consulPort = PortFinder.GetRandomPort();
        const string serviceName = "web";
        var servicePort = PortFinder.GetRandomPort();
        var serviceEntryOne = new ServiceEntry
        {
            Service = new AgentService
            {
                Service = serviceName,
                Address = "localhost",
                Port = servicePort,
                ID = "web_90_0_2_224_8080",
                Tags = ["version-v1"],
            },
        };
        var route = new FileDynamicRoute()
        {
            ServiceName = serviceName,
            RateLimitOptions = new()
            {
                EnableRateLimiting = true,
                ClientWhitelist = [],
                Limit = 3,
                Period = "1s",
                Wait = "1s",
            },
        };

        var consulConfig = GivenDiscoveryConfiguration([], consulPort);
        consulConfig.DynamicRoutes.Add(route);
        consulConfig.GlobalConfiguration.RateLimitOptions = new()
        {
            ClientIdHeader = "ClientId",
            StatusCode = StatusCodes.Status428PreconditionRequired,
        };
        consulConfig.GlobalConfiguration.DownstreamScheme = "http";

        var configuration = GivenDiscoveryConfiguration([], consulPort);
        var upstreamPath = $"/{serviceName}/something"; // dynamic route path
        this.Given(x => x.GivenThereIsAServiceRunningOn(servicePort, "/something", HttpStatusCode.OK, "Hello from Laura"))
            .And(x => GivenTheConsulConfigurationIs(consulConfig))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort, serviceName))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntryOne))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => x.GivenOcelotIsRunningUsingConsulToStoreConfig())
            .When(x => WhenIGetUrlOnTheApiGatewayMultipleTimes(upstreamPath, 1))
            .Then(x => ThenTheStatusCodeShouldBe(200))
            .When(x => WhenIGetUrlOnTheApiGatewayMultipleTimes(upstreamPath, 2))
            .Then(x => ThenTheStatusCodeShouldBe(200))
            .When(x => WhenIGetUrlOnTheApiGatewayMultipleTimes(upstreamPath, 1))
            .Then(x => ThenTheStatusCodeShouldBe(428))
        .BDDfy();
    }

    private async Task ThenTheConfigIsUpdatedInOcelot(string url)
    {
        var updated = await Wait.For(10_000).UntilAsync(async (ct) =>
        {
            await WhenIGetUrlOnTheApiGateway(url);
            // ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
            return response.StatusCode == HttpStatusCode.OK;
            // ThenTheResponseBodyShouldBe("Hello from Laura");
        }, CancelMe);
        updated.ShouldBeTrue();
    }

    private void GivenTheConsulConfigurationIs(FileConfiguration config)
    {
        _consulConfig = config;
    }

    private void GivenTheServicesAreRegisteredWithConsul(params ServiceEntry[] serviceEntries)
    {
        foreach (var serviceEntry in serviceEntries)
        {
            _consulServices.Add(serviceEntry);
        }
    }

    private Task GivenOcelotIsRunningUsingConsulToStoreConfig()
    {
        static void WithConsulToStoreConfig(IServiceCollection services)
            => services.AddOcelot().AddConsul().AddConfigStoredInConsul();
        GivenOcelotIsRunning(WithConsulToStoreConfig);
        return Task.Delay(1250, CancelMe);
    }

    private void GivenThereIsAFakeConsulServiceDiscoveryProvider(int port, string serviceName)
    {
        handler.GivenThereIsAServiceRunningOn(port, async context =>
        {
            if (context.Request.Method.Equals(HttpMethods.Get, StringComparison.CurrentCultureIgnoreCase) && context.Request.Path.Value == "/v1/kv/InternalConfiguration")
            {
                var json = JsonConvert.SerializeObject(_consulConfig);
                var bytes = Encoding.UTF8.GetBytes(json);
                var base64 = Convert.ToBase64String(bytes);
                var kvp = new FakeConsulGetResponse(base64);
                json = JsonConvert.SerializeObject(new[] { kvp });
                context.Response.Headers.Append("Content-Type", "application/json");
                await context.Response.WriteAsync(json, context.RequestAborted);
            }
            else if (context.Request.Method.Equals(HttpMethods.Put, StringComparison.CurrentCultureIgnoreCase) && context.Request.Path.Value == "/v1/kv/InternalConfiguration")
            {
                try
                {
                    var reader = new StreamReader(context.Request.Body);

                    // Synchronous operations are disallowed. Call ReadAsync or set AllowSynchronousIO to true instead.
                    // var json = reader.ReadToEnd();                                            
                    var json = await reader.ReadToEndAsync(context.RequestAborted);
                    _consulConfig = JsonConvert.DeserializeObject<FileConfiguration>(json);
                    var response = JsonConvert.SerializeObject(true);
                    await context.Response.WriteAsync(response, context.RequestAborted);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
            else if (context.Request.Path.Value == $"/v1/health/service/{serviceName}")
            {
                var json = JsonConvert.SerializeObject(_consulServices);
                context.Response.Headers.Append("Content-Type", "application/json");
                await context.Response.WriteAsync(json, context.RequestAborted);
            }
        });
    }

    public class FakeConsulGetResponse(string value)
    {
        public int CreateIndex => 100;
        public int ModifyIndex => 200;
        public int LockIndex => 200;
        public string Key => "InternalConfiguration";
        public int Flags => 0;
        public string Value { get; } = value;
        public string Session => "adf4238a-882b-9ddc-4a9d-5b6758e4159e";
    }

    private void GivenThereIsAServiceRunningOn(int port, string basePath, HttpStatusCode statusCode, string responseBody)
    {
        Task MapStatus(HttpContext context)
        {
            context.Response.StatusCode = (int)statusCode;
            return context.Response.WriteAsync(responseBody, context.RequestAborted);
        }
        handler.GivenThereIsAServiceRunningOn(port, basePath, MapStatus);
    }

    private class FakeCache : IOcelotCache<FileConfiguration>
    {
        public FileConfiguration Get(string key, string region) => throw new NotImplementedException();
        public void ClearRegion(string region) => throw new NotImplementedException();
        public bool TryGetValue(string key, string region, out FileConfiguration value) => throw new NotImplementedException();
        public bool Add(string key, FileConfiguration value, string region, TimeSpan ttl) => throw new NotImplementedException();
        public FileConfiguration AddOrUpdate(string key, FileConfiguration value, string region, TimeSpan ttl) => throw new NotImplementedException();
    }
}
