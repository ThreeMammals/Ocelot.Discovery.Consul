using Consul;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ocelot.Discovery.Consul.Acceptance;

public sealed class ConfigurationInConsulTests : ConsulSteps
{
    private FileConfiguration _config;
    private readonly List<ServiceEntry> _consulServices = [];

    [Fact]
    [Trait("Feat", "152")] // https://github.com/ThreeMammals/Ocelot/issues/152
    [Trait("PR", "153")] // https://github.com/ThreeMammals/Ocelot/pull/153
    [Trait("Release", "2.0.4")] // https://github.com/ThreeMammals/Ocelot/releases/tag/2.0.4
    [Trait("Commit", "48b5a32")] // https://github.com/ThreeMammals/Ocelot/commit/48b5a326768b695f988e105967e39d77f45e3811
    public async Task Should_return_response_200_with_simple_url_when_using_jsonserialized_cache()
    {
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var route = GivenDefaultRoute(servicePort);
        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        GivenThereIsAFakeConsulServiceDiscoveryProvider(consulPort);
        GivenThereIsAServiceRunningOn(servicePort, "Hello from Charalampos");
        GivenThereIsAConfiguration(configuration);
        GivenOcelotIsRunning(WithConsulToStoreConfigAndJsonSerializedCache);
        await WhenIGetUrlOnTheApiGateway("/");
        ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
        await ThenTheResponseBodyShouldBeAsync("Hello from Charalampos");
    }

    private static void WithConsulToStoreConfigAndJsonSerializedCache(IServiceCollection services)
        => services.AddOcelot().AddConsul().AddConfigStoredInConsul();

    private void GivenThereIsAFakeConsulServiceDiscoveryProvider(int consulPort, [CallerMemberName] string serviceName = null)
    {
        handler.GivenThereIsAServiceRunningOn(consulPort, MapConsulAPI);
        async Task MapConsulAPI(HttpContext context)
        {
            if (context.RequestAborted.IsCancellationRequested || CancelMe.IsCancellationRequested)
                return;

            if (context.Request.Method.Equals(HttpMethods.Get, StringComparison.InvariantCultureIgnoreCase) &&
                context.Request.Path.Value == "/v1/kv/InternalConfiguration")
            {
                var json = JsonConvert.SerializeObject(_config);
                var bytes = Encoding.UTF8.GetBytes(json);
                var base64 = Convert.ToBase64String(bytes);
                var kvp = new FakeConsulGetResponse(base64);

                // await context.Response.WriteJsonAsync(new[] { kvp });
            }
            else if (context.Request.Method.Equals(HttpMethods.Put, StringComparison.InvariantCultureIgnoreCase) &&
                context.Request.Path.Value == "/v1/kv/InternalConfiguration")
            {
                try
                {
                    var reader = new StreamReader(context.Request.Body);

                    // Synchronous operations are disallowed. Call ReadAsync or set AllowSynchronousIO to true instead.
                    // var json = reader.ReadToEnd();                                            
                    var json = await reader.ReadToEndAsync(context.RequestAborted);
                    _config = JsonConvert.DeserializeObject<FileConfiguration>(json);
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
                //await context.Response.WriteJsonAsync(_consulServices);
            }
        }
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
}
