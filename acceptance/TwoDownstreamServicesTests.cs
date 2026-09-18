using Consul;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Ocelot.Discovery.Consul.Acceptance;

public sealed class TwoDownstreamServicesTests : ConsulSteps
{
    private readonly List<ServiceEntry> _serviceEntries = [];

    [Fact]
    [Trait("Bug", "194")] // https://github.com/ThreeMammals/Ocelot/issues/194
    [Trait("PR", "197")] // https://github.com/ThreeMammals/Ocelot/pull/197
    [Trait("Release", "2.0.11")] // https://github.com/ThreeMammals/Ocelot/releases/tag/2.0.11
    [Trait("Commit", "31f526d")] // https://github.com/ThreeMammals/Ocelot/commit/31f526d3cd3576079acf1fb72dbc31f71211c494
    public async Task Should_fix_issue_194()
    {
        var consulPort = PortFinder.GetRandomPort();
        var servicePort1 = PortFinder.GetRandomPort();
        var servicePort2 = PortFinder.GetRandomPort();
        var route1 = GivenRoute(servicePort1, "/api/user/{user}", "/api/user/{user}");
        var route2 = GivenRoute(servicePort2, "/api/product/{product}", "/api/product/{product}");
        var configuration = GivenDiscoveryConfiguration([route1, route2], consulPort, scheme: Uri.UriSchemeHttps);
        GivenProductServiceIsRunning(servicePort1, "/api/user/info", HttpStatusCode.OK, "user");
        GivenProductServiceIsRunning(servicePort2, "/api/product/info", HttpStatusCode.OK, "product");
        GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort);
        GivenThereIsAConfiguration(configuration);
        GivenOcelotIsRunning(WithConsul);
        await WhenIGetUrlOnTheApiGateway("/api/user/info?id=1");
        ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
        await ThenTheResponseBodyShouldBeAsync("user");
        await WhenIGetUrlOnTheApiGateway("/api/product/info?id=1");
        ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
        await ThenTheResponseBodyShouldBeAsync("product");
    }

    private void GivenThereIsAFakeConsulServiceDiscoveryProvider(int port)
        => handler.GivenThereIsAServiceRunningOn(port, MapPath);
    private Task MapPath(HttpContext context)
    {
        if (context.Request.Path.Value == "/v1/health/service/product")
        {
            var json = JsonConvert.SerializeObject(_serviceEntries);
            context.Response.Headers.Append("Content-Type", "application/json");
            return context.Response.WriteAsync(json, context.RequestAborted);
        }
        return context.Response.WriteAsync(string.Empty, context.RequestAborted);
    }

    private void GivenProductServiceIsRunning(int port, string basePath, HttpStatusCode statusCode, string responseBody)
    {
        handler.GivenThereIsAServiceRunningOn(port, basePath, context =>
        {
            var downstreamPath = !string.IsNullOrEmpty(context.Request.PathBase.Value) ? context.Request.PathBase.Value : context.Request.Path.Value;
            bool oK = downstreamPath == basePath;
            context.Response.StatusCode = oK ? (int)statusCode : (int)HttpStatusCode.NotFound;
            return context.Response.WriteAsync(oK ? responseBody : "downstream path didn't match base path", context.RequestAborted);
        });
    }
}
