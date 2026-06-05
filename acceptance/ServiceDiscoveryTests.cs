using Consul;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Ocelot.Configuration;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.Infrastructure;
using Ocelot.LoadBalancer.Balancers;
using Ocelot.LoadBalancer.Creators;
using Ocelot.LoadBalancer.Interfaces;
using Ocelot.Logging;
using Ocelot.ServiceDiscovery.Providers;
using Ocelot.Testing.LoadBalancer;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TestStack.BDDfy;

namespace Ocelot.Discovery.Consul.Acceptance;

/// <summary>
/// Tests for the <see cref="Consul"/> provider.
/// </summary>
public sealed partial class ServiceDiscoveryTests : ConsulSteps
{
    private readonly ServiceHandler _consulHandler = new();
    private readonly List<ServiceEntry> _consulServices = [];
    private readonly List<Node> _consulNodes = [];

    private string _receivedToken;
    private volatile int _counterConsul;
    private volatile int _counterNodes;

    public override void Dispose()
    {
        _consulHandler.Dispose();
        base.Dispose();
    }

    [Fact]
    [Trait("Feat", "17")] // https://github.com/ThreeMammals/Ocelot/issues/17
    [Trait("PR", "28")] // https://github.com/ThreeMammals/Ocelot/pull/28
    [Trait("Release", "1.2.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/1.2.0
    [Trait("Commit", "9e9303c")] // https://github.com/ThreeMammals/Ocelot/commit/9e9303c25f2efa8be4ac6e6687c06362e10d8bab
    public void ShouldDiscoverServicesInConsulAndLoadBalanceByLeastConnectionWhenConfigInRoute()
    {
        const string serviceName = "product";
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(2);
        var serviceEntries = ports.Select(port => GivenServiceEntry(port, serviceName: serviceName)).ToArray();
        var route = GivenDiscoveryRoute(serviceName: serviceName, loadBalancerType: nameof(LeastConnection));
        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, serviceName))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntries))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently("/", 50))
            .Then(x => ThenAllServicesShouldHaveBeenCalledTimes(50))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(/*25*/24, /*25*/26)) // TODO Check strict assertion
        .BDDfy();
    }

    [Fact]
    [Trait("Bug", "181")] // https://github.com/ThreeMammals/Ocelot/issues/181
    [Trait("PR", "195")] // https://github.com/ThreeMammals/Ocelot/pull/195
    [Trait("Release", "2.0.9")] // https://github.com/ThreeMammals/Ocelot/releases/tag/2.0.9
    [Trait("Commit", "6992f9e")] // https://github.com/ThreeMammals/Ocelot/commit/6992f9e113de969d4dca0fcab7adb9a730322b00
    public void ShouldSendRequestToServiceAfterItBecomesAvailableInConsul()
    {
        const string serviceName = "product";
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(2);
        var serviceEntries = ports.Select(port => GivenServiceEntry(port, serviceName: serviceName)).ToArray();
        var route = GivenDiscoveryRoute(serviceName: serviceName);
        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(_ => GivenMultipleServiceInstancesAreRunning(urls, serviceName))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntries))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .And(x => WhenIGetUrlOnTheApiGatewayConcurrently("/", 10))
            .And(x => ThenAllServicesShouldHaveBeenCalledTimes(10))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(/*5*/4, /*5*/6)) // TODO Check strict assertion
            .And(x => x.WhenIRemoveAService(serviceEntries[1])) // 2nd entry
            .And(x => x.GivenIResetCounters())
            .And(x => WhenIGetUrlOnTheApiGatewayConcurrently("/", 10))
            .And(x => ThenServicesShouldHaveBeenCalledTimes(10, 0)) // 2nd is offline
            .And(x => x.WhenIAddAServiceBackIn(serviceEntries[1])) // 2nd entry
            .And(x => x.GivenIResetCounters())
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently("/", 10))
            .Then(x => ThenAllServicesShouldHaveBeenCalledTimes(10))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(/*5*/4, /*5*/6)) // TODO Check strict assertion
        .BDDfy();
    }

    private static readonly string[] VersionV1Tags = ["version-v1"];
    private static readonly string[] GetVsOptionsMethods = ["Get", "Options"];

    [Fact]
    [Trait("Feat", "201")] // https://github.com/ThreeMammals/Ocelot/issues/201
    [Trait("Bug", "213")] // https://github.com/ThreeMammals/Ocelot/issues/213
    [Trait("PR", "211")]  // https://github.com/ThreeMammals/Ocelot/pull/211
    [Trait("Release", "3.0.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/3.0.0
    [Trait("Commit", "9d0a7f5")] // https://github.com/ThreeMammals/Ocelot/commit/9d0a7f5961e48a9340e6552e6738dda954218cfa
    public void ShouldHandleRequestToConsulForDownstreamServiceAndMakeRequest()
    {
        const string serviceName = "web";
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var serviceEntryOne = GivenServiceEntry(servicePort, "localhost", "web_90_0_2_224_8080", VersionV1Tags, serviceName);
        var route = GivenDiscoveryRoute("/api/home", "/home", serviceName, httpMethods: GetVsOptionsMethods);
        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        this.Given(x => GivenThereIsAServiceRunningOn(DownstreamUrl(servicePort), "/api/home", "Hello from Laura"))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntryOne))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGateway("/home"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "295")] // https://github.com/ThreeMammals/Ocelot/issues/295
    [Trait("PR", "307")]  // https://github.com/ThreeMammals/Ocelot/pull/307
    [Trait("Release", "5.5.1")] // https://github.com/ThreeMammals/Ocelot/releases/tag/5.5.1
    [Trait("Commit", "982eebf")] // https://github.com/ThreeMammals/Ocelot/commit/982eebfc74217a5fef34321c97f91cd1afaa9bed
    public void ShouldUseAclTokenToMakeRequestToConsul()
    {
        const string serviceName = "web";
        const string token = "abctoken";
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var serviceEntry = GivenServiceEntry(servicePort, "localhost", "web_90_0_2_224_8080", VersionV1Tags, serviceName);
        var route = GivenDiscoveryRoute("/api/home", "/home", serviceName, httpMethods: GetVsOptionsMethods);

        var configuration = GivenDiscoveryConfiguration([route], consulPort);
        configuration.GlobalConfiguration.ServiceDiscoveryProvider.Token = token;

        this.Given(x => GivenThereIsAServiceRunningOn(DownstreamUrl(servicePort), "/api/home", "Hello from Laura"))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntry))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGateway("/home"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
            .And(x => x.ThenTheTokenIs(token))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "340")] // https://github.com/ThreeMammals/Ocelot/issues/340
    [Trait("PR", "351")]  // https://github.com/ThreeMammals/Ocelot/pull/351
    [Trait("Release", "7.0.1")] // https://github.com/ThreeMammals/Ocelot/releases/tag/7.0.1
    [Trait("Commit", "1e2e953")] // https://github.com/ThreeMammals/Ocelot/commit/1e2e953b2cef4431b42288a9d89b1d97eff757b4
    public void ShouldHandleRequestToConsulForDownstreamServiceAndMakeRequestWhenDynamicRoutingWithNoRoutes()
    {
        const string serviceName = "web";
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var serviceEntry = GivenServiceEntry(servicePort, "localhost", "web_90_0_2_224_8080", VersionV1Tags, serviceName);

        var configuration = GivenDiscoveryConfiguration(NoRoutes, consulPort); // no routes
        configuration.GlobalConfiguration.DownstreamScheme = "http";
        configuration.GlobalConfiguration.HttpHandlerOptions = new()
        {
            AllowAutoRedirect = true,
            UseCookieContainer = true,
            UseTracing = false,
        };

        this.Given(x => GivenThereIsAServiceRunningOn(DownstreamUrl(servicePort), "/something", "Hello from Laura"))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntry))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGateway("/web/something"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "340")] // https://github.com/ThreeMammals/Ocelot/issues/340
    [Trait("PR", "351")]  // https://github.com/ThreeMammals/Ocelot/pull/351
    [Trait("Release", "7.0.1")] // https://github.com/ThreeMammals/Ocelot/releases/tag/7.0.1
    [Trait("Commit", "1e2e953")] // https://github.com/ThreeMammals/Ocelot/commit/1e2e953b2cef4431b42288a9d89b1d97eff757b4
    public void ShouldUseConsulServiceDiscoveryAndLoadBalanceRequestWhenDynamicRoutingWithNoRoutes()
    {
        const string serviceName = "product";
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(2);
        var serviceEntries = ports.Select(port => GivenServiceEntry(port, serviceName: serviceName)).ToArray();

        var configuration = GivenDiscoveryConfiguration(NoRoutes, consulPort); // !!!
        configuration.GlobalConfiguration.LoadBalancerOptions = new() { Type = nameof(LeastConnection) };
        configuration.GlobalConfiguration.DownstreamScheme = "http";

        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, serviceName))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntries))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently($"/{serviceName}/", 50))
            .Then(x => ThenAllServicesShouldHaveBeenCalledTimes(50))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(/*25*/24, /*25*/26)) // TODO Check strict assertion
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "374")] // https://github.com/ThreeMammals/Ocelot/issues/374
    [Trait("PR", "392")]  // https://github.com/ThreeMammals/Ocelot/pull/392
    [Trait("Release", "7.0.5")] // https://github.com/ThreeMammals/Ocelot/releases/tag/7.0.5
    [Trait("Commit", "0f2a9c1")] // https://github.com/ThreeMammals/Ocelot/commit/0f2a9c1d0d22d11697d9ebaabd75316ab4465678
    public void ShouldPollConsulForDownstreamServiceAndMakeRequest()
    {
        const string serviceName = "web";
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort();
        var serviceEntry = GivenServiceEntry(servicePort, "localhost", $"web_90_0_2_224_{servicePort}", VersionV1Tags, serviceName);
        var route = GivenDiscoveryRoute("/api/home", "/home", serviceName, httpMethods: GetVsOptionsMethods);
        var configuration = GivenDiscoveryConfiguration([route], consulPort, provider: nameof(PollConsul));
        configuration.GlobalConfiguration.ServiceDiscoveryProvider.PollingInterval = 0; // start immediately

        this.Given(x => GivenThereIsAServiceRunningOn(DownstreamUrl(servicePort), "/api/home", "Hello from Laura"))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntry))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGatewayWaitingForTheResponseToBeOk("/home"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
        .BDDfy();
    }

    private async Task WhenIGetUrlOnTheApiGatewayWaitingForTheResponseToBeOk(string url)
    {
        var result = await Wait.For(2_000).UntilAsync(async (ct) =>
        {
            response = await ocelotClient.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }, CancelMe);
        result.ShouldBeTrue();
    }

    [Theory]
    [Trait("Bug", "849")] // https://github.com/ThreeMammals/Ocelot/issues/849
    [Trait("Bug", "1496")] // https://github.com/ThreeMammals/Ocelot/issues/1496
    [Trait("PR", "1944")] // https://github.com/ThreeMammals/Ocelot/pull/1944
    [Trait("Release", "23.1.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/23.1.0
    [Trait("Commit", "8845d1b")] // https://github.com/ThreeMammals/Ocelot/commit/8845d1b98d2c3b0bd633ebd2b526b923687edd33
    [InlineData(nameof(NoLoadBalancer))]
    [InlineData(nameof(RoundRobin))]
    [InlineData(nameof(LeastConnection))]
    [InlineData(nameof(CookieStickySessions))]
    public void ShouldUseConsulServiceDiscoveryWhenThereAreTwoUpstreamHosts(string loadBalancerType)
    {
        // Simulate two DIFFERENT downstream services (e.g. product services for US and EU markets)
        // with different ServiceNames (e.g. product-us and product-eu),
        // UpstreamHost is used to determine which ServiceName to use when making a request to Consul (e.g. Host: us-shop goes to product-us) 
        const string serviceNameUS = "product-us";
        const string serviceNameEU = "product-eu";
        string[] tagsUS = ["US"], tagsEU = ["EU"];
        var consulPort = PortFinder.GetRandomPort();
        var servicePortUS = PortFinder.GetRandomPort();
        var servicePortEU = PortFinder.GetRandomPort();
        const string upstreamHostUS = "us-shop";
        const string upstreamHostEU = "eu-shop";
        const string responseBodyUS = "Phone chargers with US plug";
        const string responseBodyEU = "Phone chargers with EU plug";
        var serviceEntryUS = GivenServiceEntry(servicePortUS, serviceName: serviceNameUS, tags: tagsUS);
        var serviceEntryEU = GivenServiceEntry(servicePortEU, serviceName: serviceNameEU, tags: tagsEU);
        var routeUS = GivenDiscoveryRoute("/products", "/", serviceNameUS, loadBalancerType, upstreamHostUS);
        var routeEU = GivenDiscoveryRoute("/products", "/", serviceNameEU, loadBalancerType, upstreamHostEU);
        var configuration = GivenDiscoveryConfiguration([routeUS, routeEU], consulPort);
        bool isStickySession = loadBalancerType == nameof(CookieStickySessions);
        var sessionCookieUS = isStickySession ? new CookieHeaderValue(routeUS.LoadBalancerOptions.Key, Guid.NewGuid().ToString()) : null;
        var sessionCookieEU = isStickySession ? new CookieHeaderValue(routeEU.LoadBalancerOptions.Key, Guid.NewGuid().ToString()) : null;

        // Ocelot request for http://us-shop/ should find 'product-us' in Consul, call /products and return "Phone chargers with US plug"
        // Ocelot request for http://eu-shop/ should find 'product-eu' in Consul, call /products and return "Phone chargers with EU plug"
        this.Given(x => handler.GivenThereIsAServiceRunningOn(servicePortUS, "/products", MapGet("/products", responseBodyUS)))
            .Given(x => handler.GivenThereIsAServiceRunningOn(servicePortEU, "/products", MapGet("/products", responseBodyEU)))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntryUS, serviceEntryEU))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => x.WhenIGetUrlOfRequestComingFromHost(routeUS.UpstreamPathTemplate, upstreamHostUS, sessionCookieUS),
                    "When I get US shop for the first time")
            .Then(x => x.ThenConsulShouldHaveBeenCalledTimes(1))
            .And(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe(responseBodyUS))
            .When(x => x.WhenIGetUrlOfRequestComingFromHost(routeEU.UpstreamPathTemplate, upstreamHostEU, sessionCookieEU),
                    "When I get EU shop for the first time")
            .Then(x => x.ThenConsulShouldHaveBeenCalledTimes(2))
            .And(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe(responseBodyEU))
            .When(x => x.WhenIGetUrlOfRequestComingFromHost(routeUS.UpstreamPathTemplate, upstreamHostUS, sessionCookieUS),
                    "When I get US shop again")
            .Then(x => x.ThenConsulShouldHaveBeenCalledTimes(isStickySession ? 2 : 3)) // sticky sessions use cache, so Consul shouldn't be called
            .And(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe(responseBodyUS))
            .When(x => x.WhenIGetUrlOfRequestComingFromHost(routeEU.UpstreamPathTemplate, upstreamHostEU, sessionCookieEU),
                    "When I get EU shop again")
            .Then(x => x.ThenConsulShouldHaveBeenCalledTimes(isStickySession ? 2 : 4)) // sticky sessions use cache, so Consul shouldn't be called
            .And(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe(responseBodyEU))
        .BDDfy();
    }

    [Fact]
    [Trait("Bug", "954")] // https://github.com/ThreeMammals/Ocelot/issues/954
    [Trait("Bug", "957")] // https://github.com/ThreeMammals/Ocelot/issues/957
    [Trait("Bug", "1026")] // https://github.com/ThreeMammals/Ocelot/issues/1026
    [Trait("PR", "2067")] // https://github.com/ThreeMammals/Ocelot/pull/2067
    [Trait("Release", "23.3.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/23.3.0
    [Trait("Commit", "34cb3eb")] // https://github.com/ThreeMammals/Ocelot/commit/34cb3ebf9768ac8cd8d2c75139da2123e23fdba4
    public void ShouldReturnServiceAddressByOverriddenServiceBuilderWhenThereIsANode()
    {
        const string serviceName = "OpenTestService";
        string[] methods = [HttpMethods.Post, HttpMethods.Get];
        var consulPort = PortFinder.GetRandomPort();
        var servicePort = PortFinder.GetRandomPort(); // 9999
        var serviceEntry = GivenServiceEntry(servicePort,
            id: "OPEN_TEST_01",
            serviceName: serviceName,
            tags: [serviceName]);
        var serviceNode = new Node() { Name = "n1" }; // cornerstone of the bug
        serviceEntry.Node = serviceNode;
        var route = GivenDiscoveryRoute("/api/{url}", "/open/{url}", serviceName, httpMethods: methods);
        var configuration = GivenDiscoveryConfiguration([route], consulPort);

        this.Given(x => GivenThereIsAServiceRunningOnPath(servicePort, "/api/home", "Hello from Raman"))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntry))
            .And(x => x.GivenTheServiceNodesAreRegisteredWithConsul(serviceNode))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul)) // default services registration results with the bug: "n1" host issue
            .When(x => WhenIGetUrlOnTheApiGateway("/open/home"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.BadGateway))
            .And(x => ThenTheResponseBodyShouldBe(""))
            .And(x => ThenConsulShouldHaveBeenCalledTimes(1))
            .And(x => ThenConsulNodesShouldHaveBeenCalledTimes(1))

            // Override default service builder
            .Given(x => GivenOcelotIsRunning(WithConsulServiceBuilder))
            .When(x => WhenIGetUrlOnTheApiGateway("/open/home"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Raman"))
            .And(x => ThenConsulShouldHaveBeenCalledTimes(2))
            .And(x => ThenConsulNodesShouldHaveBeenCalledTimes(2))
        .BDDfy();
    }

    private static readonly string[] Bug2119ServiceNames = new string[] { "ProjectsService", "CustomersService" };
    private readonly ILoadBalancer[] _lbAnalyzers = new ILoadBalancer[Bug2119ServiceNames.Length]; // emulate LoadBalancerHouse's collection

    private TLoadBalancer GetAnalyzer<TLoadBalancer, TLoadBalancerCreator>(DownstreamRoute route, IServiceDiscoveryProvider provider)
        where TLoadBalancer : class, ILoadBalancer
        where TLoadBalancerCreator : class, ILoadBalancerCreator, new()
    {
        //lock (LoadBalancerHouse.SyncRoot) // Note, synch locking is implemented in LoadBalancerHouse
        int index = Array.IndexOf(Bug2119ServiceNames, route.ServiceName); // LoadBalancerHouse should return different balancers for different service names
        _lbAnalyzers[index] ??= new TLoadBalancerCreator().Create(route, provider).Data;
        return (TLoadBalancer)_lbAnalyzers[index];
    }

    private void WithLbAnalyzer<TLoadBalancer, TLoadBalancerCreator>(IServiceCollection services)
        where TLoadBalancer : class, ILoadBalancer
        where TLoadBalancerCreator : class, ILoadBalancerCreator, new()
        => services.AddOcelot().AddConsul().AddCustomLoadBalancer(GetAnalyzer<TLoadBalancer, TLoadBalancerCreator>);

    [Theory]
    [Trait("Bug", "2119")] // https://github.com/ThreeMammals/Ocelot/issues/2119
    [Trait("PR", "2151")] // https://github.com/ThreeMammals/Ocelot/pull/2151
    [Trait("Release", "23.3.4")] // https://github.com/ThreeMammals/Ocelot/releases/tag/23.3.4
    [Trait("Commit", "09f2b1a")] // https://github.com/ThreeMammals/Ocelot/commit/09f2b1afe15f4d7f70f7175ca41ada5bfaaf1c6d
    [InlineData(nameof(NoLoadBalancer))]
    [InlineData(nameof(RoundRobin))]
    [InlineData(nameof(LeastConnection))] // original scenario
    public void ShouldReturnDifferentServicesWhenThereAre2SequentialRequestsToDifferentServices(string loadBalancer)
    {
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(Bug2119ServiceNames.Length);
        var service1 = GivenServiceEntry(ports[0], serviceName: Bug2119ServiceNames[0]);
        var service2 = GivenServiceEntry(ports[1], serviceName: Bug2119ServiceNames[1]);
        var route1 = GivenDiscoveryRoute("/{all}", "/projects/{all}", serviceName: Bug2119ServiceNames[0], loadBalancerType: loadBalancer);
        var route2 = GivenDiscoveryRoute("/{all}", "/customers/{all}", serviceName: Bug2119ServiceNames[1], loadBalancerType: loadBalancer);
        route1.UpstreamHttpMethod = route2.UpstreamHttpMethod = new() { HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete };
        var configuration = GivenDiscoveryConfiguration([route1, route2], consulPort);
        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, Bug2119ServiceNames))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(service1, service2))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))

            // Step 1
            .When(x => WhenIGetUrlOnTheApiGateway("/projects/api/projects"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenServiceShouldHaveBeenCalledTimes(0, 1))
            .And(x => x.ThenTheResponseBodyShouldBe($"1^:^{Bug2119ServiceNames[0]}")) // !

            // Step 2
            .When(x => WhenIGetUrlOnTheApiGateway("/customers/api/customers"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenServiceShouldHaveBeenCalledTimes(1, 1))
            .And(x => x.ThenTheResponseBodyShouldBe($"1^:^{Bug2119ServiceNames[1]}")) // !!

            // Finally
            .Then(x => ThenAllStatusCodesShouldBe(HttpStatusCode.OK))
            .And(x => ThenAllServicesShouldHaveBeenCalledTimes(2))
            .And(x => ThenServicesShouldHaveBeenCalledTimes(1, 1))
        .BDDfy();
    }

    [Theory]
    [Trait("Bug", "2119")] // https://github.com/ThreeMammals/Ocelot/issues/2119
    [Trait("PR", "2151")] // https://github.com/ThreeMammals/Ocelot/pull/2151
    [Trait("Release", "23.3.4")] // https://github.com/ThreeMammals/Ocelot/releases/tag/23.3.4
    [Trait("Commit", "09f2b1a")] // https://github.com/ThreeMammals/Ocelot/commit/09f2b1afe15f4d7f70f7175ca41ada5bfaaf1c6d
    [InlineData(false, nameof(NoLoadBalancer))]
    [InlineData(false, nameof(LeastConnection))] // original scenario, clean config
    [InlineData(true, nameof(LeastConnectionAnalyzer))] // extended scenario using analyzer
    [InlineData(false, nameof(RoundRobin))]
    [InlineData(true, nameof(RoundRobinAnalyzer))]
    public void ShouldReturnDifferentServicesWhenSequentiallyRequestingToDifferentServices(bool withAnalyzer, string loadBalancer)
    {
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(Bug2119ServiceNames.Length);
        var service1 = GivenServiceEntry(ports[0], serviceName: Bug2119ServiceNames[0]);
        var service2 = GivenServiceEntry(ports[1], serviceName: Bug2119ServiceNames[1]);
        var route1 = GivenDiscoveryRoute("/{all}", "/projects/{all}", serviceName: Bug2119ServiceNames[0], loadBalancerType: loadBalancer);
        var route2 = GivenDiscoveryRoute("/{all}", "/customers/{all}", serviceName: Bug2119ServiceNames[1], loadBalancerType: loadBalancer);
        route1.UpstreamHttpMethod = route2.UpstreamHttpMethod = [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete];
        var configuration = GivenDiscoveryConfiguration([route1, route2], consulPort);
        var urls = ports.Select(DownstreamUrl).ToArray();
        Func<int, Task> requestToProjectsAndThenRequestToCustomersAndAssert = async (i) =>
        {
            // Step 1
            int count = i + 1;
            await WhenIGetUrlOnTheApiGateway("/projects/api/projects");
            ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
            ThenServiceShouldHaveBeenCalledTimes(0, count);
            ThenTheResponseBodyShouldBe($"{count}^:^{Bug2119ServiceNames[0]}", $"i is {i}");
            Responses[2 * i] = response;

            // Step 2
            await WhenIGetUrlOnTheApiGateway("/customers/api/customers");
            ThenTheStatusCodeShouldBe(HttpStatusCode.OK);
            ThenServiceShouldHaveBeenCalledTimes(1, count);
            ThenTheResponseBodyShouldBe($"{count}^:^{Bug2119ServiceNames[1]}", $"i is {i}");
            Responses[(2 * i) + 1] = response;
        };
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, Bug2119ServiceNames)) // service names as responses
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(service1, service2))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(withAnalyzer ? WithLbAnalyzer(loadBalancer) : WithConsul))
            .When(x => WhenIDoActionMultipleTimes(50, requestToProjectsAndThenRequestToCustomersAndAssert))
            .Then(x => ThenAllStatusCodesShouldBe(HttpStatusCode.OK))
            .And(x => x.ThenResponsesShouldHaveBodyFromDifferentServices(ports, Bug2119ServiceNames)) // !!!
            .And(x => ThenAllServicesShouldHaveBeenCalledTimes(100))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(50, 50))
            .And(x => ThenServicesShouldHaveBeenCalledTimes(50, 50)) // strict assertion
        .BDDfy();
    }

    [Theory]
    [Trait("Bug", "2119")] // https://github.com/ThreeMammals/Ocelot/issues/2119
    [Trait("PR", "2151")] // https://github.com/ThreeMammals/Ocelot/pull/2151
    [Trait("Release", "23.3.4")] // https://github.com/ThreeMammals/Ocelot/releases/tag/23.3.4
    [Trait("Commit", "09f2b1a")] // https://github.com/ThreeMammals/Ocelot/commit/09f2b1afe15f4d7f70f7175ca41ada5bfaaf1c6d
    [InlineData(false, nameof(NoLoadBalancer))]
    [InlineData(false, nameof(LeastConnection))] // original scenario, clean config
    [InlineData(true, nameof(LeastConnectionAnalyzer))] // extended scenario using analyzer
    [InlineData(false, nameof(RoundRobin))]
    [InlineData(true, nameof(RoundRobinAnalyzer))]
    public void ShouldReturnDifferentServicesWhenConcurrentlyRequestingToDifferentServices(bool withAnalyzer, string loadBalancer)
    {
        const int total = 100; // concurrent requests
        var consulPort = PortFinder.GetRandomPort();
        var ports = PortFinder.GetPorts(Bug2119ServiceNames.Length);
        var service1 = GivenServiceEntry(ports[0], serviceName: Bug2119ServiceNames[0]);
        var service2 = GivenServiceEntry(ports[1], serviceName: Bug2119ServiceNames[1]);
        var route1 = GivenDiscoveryRoute("/{all}", "/projects/{all}", serviceName: Bug2119ServiceNames[0], loadBalancerType: loadBalancer);
        var route2 = GivenDiscoveryRoute("/{all}", "/customers/{all}", serviceName: Bug2119ServiceNames[1], loadBalancerType: loadBalancer);
        route1.UpstreamHttpMethod = route2.UpstreamHttpMethod = [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete];
        var configuration = GivenDiscoveryConfiguration([route1, route2], consulPort);
        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, Bug2119ServiceNames)) // service names as responses
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(service1, service2))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(withAnalyzer ? WithLbAnalyzer(loadBalancer) : WithConsul))
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently(total, "/projects/api/projects", "/customers/api/customers"))
            .Then(x => ThenAllStatusCodesShouldBe(HttpStatusCode.OK))
            .And(x => x.ThenResponsesShouldHaveBodyFromDifferentServices(ports, Bug2119ServiceNames)) // !!!
            .And(x => ThenAllServicesShouldHaveBeenCalledTimes(total))
            .And(x => ThenServiceCountersShouldMatchLeasingCounters((ILoadBalancerAnalyzer)_lbAnalyzers[0], ports, 50)) // ProjectsService
            .And(x => ThenServiceCountersShouldMatchLeasingCounters((ILoadBalancerAnalyzer)_lbAnalyzers[1], ports, 50)) // CustomersService
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(Bottom(total, ports.Length), Top(total, ports.Length)))
            .And(x => ThenServicesShouldHaveBeenCalledTimes(50, 50)) // strict assertion
        .BDDfy();
    }

    [Fact]
    [Trait("Feat", "585")] // https://github.com/ThreeMammals/Ocelot/issues/585
    [Trait("Feat", "2319")] // https://github.com/ThreeMammals/Ocelot/issues/2319
    [Trait("PR", "2324")] // https://github.com/ThreeMammals/Ocelot/pull/2324
    [Trait("Release", "24.1.0")] // https://github.com/ThreeMammals/Ocelot/releases/tag/24.1.0
    [Trait("Commit", "f758ba7")] // https://github.com/ThreeMammals/Ocelot/commit/f758ba7b1b79054c455be72b17ef30419032cf72
    public void ShouldApplyGlobalLoadBalancerOptionsForAllDynamicRoutes()
    {
        var ports = PortFinder.GetPorts(5);
        var serviceName = TestName(); // ServiceName();
        var serviceEntries = ports.Select(port => GivenServiceEntry(port, serviceName: serviceName)).ToArray();
        var consulPort = PortFinder.GetRandomPort();
        var configuration = GivenDiscoveryConfiguration(NoRoutes, consulPort);
        configuration.GlobalConfiguration.LoadBalancerOptions = new(nameof(RoundRobin));
        configuration.GlobalConfiguration.DownstreamScheme = Uri.UriSchemeHttp;
        configuration.Routes = []; // dynamic routing
        configuration.DynamicRoutes = []; // no dynamic routes, for ALL dynamic routes

        var urls = ports.Select(DownstreamUrl).ToArray();
        this.Given(x => GivenMultipleServiceInstancesAreRunning(urls, serviceName))
            .And(x => x.GivenThereIsAFakeConsulServiceDiscoveryProvider(DownstreamUrl(consulPort)))
            .And(x => x.GivenTheServicesAreRegisteredWithConsul(serviceEntries))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithConsul))
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently($"/{serviceName}/", 50))
            .Then(x => ThenAllServicesShouldHaveBeenCalledTimes(50))
            .And(x => ThenAllServicesCalledRealisticAmountOfTimes(9, 11)) // soft assertion
            .And(x => ThenServicesShouldHaveBeenCalledTimes(10, 10, 10, 10, 10)) // distribution by RoundRobin algorithm, aka strict assertion
        .BDDfy();
    }

    private Action<IServiceCollection> WithLbAnalyzer(string loadBalancer) => loadBalancer switch
    {
        nameof(LeastConnection) => WithLbAnalyzer<LeastConnection, LeastConnectionCreator>,
        nameof(LeastConnectionAnalyzer) => WithLbAnalyzer<LeastConnectionAnalyzer, LeastConnectionAnalyzerCreator>,
        nameof(RoundRobin) => WithLbAnalyzer<RoundRobin, RoundRobinCreator>,
        nameof(RoundRobinAnalyzer) => WithLbAnalyzer<RoundRobinAnalyzer, RoundRobinAnalyzerCreator>,
        _ => WithLbAnalyzer<LeastConnection, LeastConnectionCreator>,
    };

    private void ThenResponsesShouldHaveBodyFromDifferentServices(int[] ports, string[] serviceNames)
    {
        foreach (var response in Responses)
        {
            var headers = response.Value.Headers;
            headers.TryGetValues(HeaderNames.ServiceIndex, out var indexValues).ShouldBeTrue();
            int serviceIndex = int.Parse(indexValues.FirstOrDefault() ?? "-1");
            serviceIndex.ShouldBeGreaterThanOrEqualTo(0);

            headers.TryGetValues(HeaderNames.Host, out var hostValues).ShouldBeTrue();
            hostValues.FirstOrDefault().ShouldBe("localhost");
            headers.TryGetValues(HeaderNames.Port, out var portValues).ShouldBeTrue();
            portValues.FirstOrDefault().ShouldBe(ports[serviceIndex].ToString());

            var body = response.Value.Content.ReadAsStringAsync().Result;
            var serviceName = serviceNames[serviceIndex];
            body.ShouldNotBeNull().ShouldEndWith(serviceName);

            headers.TryGetValues(HeaderNames.Counter, out var counterValues).ShouldBeTrue();
            var counter = counterValues.ShouldNotBeNull().FirstOrDefault().ShouldNotBeNull();
            body.ShouldBe($"{counter}^:^{serviceName}");
        }
    }

    private static void WithConsulServiceBuilder(IServiceCollection services) => services
        .AddOcelot().AddConsul<MyConsulServiceBuilder>();

    public class MyConsulServiceBuilder : DefaultConsulServiceBuilder
    {
        public MyConsulServiceBuilder(IHttpContextAccessor contextAccessor, IConsulClientFactory clientFactory, IOcelotLoggerFactory loggerFactory)
            : base(contextAccessor, clientFactory, loggerFactory) { }

        protected override string GetDownstreamHost(ServiceEntry entry, Node node) => entry.Service.Address;
    }

    private static ServiceEntry GivenServiceEntry(int port, string address = null, string id = null, string[] tags = null, [CallerMemberName] string serviceName = null) => new()
    {
        Service = new AgentService
        {
            Service = serviceName,
            Address = address ?? "localhost",
            Port = port,
            ID = id ?? Guid.NewGuid().ToString(),
            Tags = tags ?? [],
        },
    };

    private FileRoute GivenDiscoveryRoute(string downstream = null, string upstream = null, [CallerMemberName] string serviceName = null, string loadBalancerType = null, string upstreamHost = null, string[] httpMethods = null)
    {
        var r = GivenRoute(0, upstream, downstream);
        r.DownstreamHostAndPorts.Clear();
        r.UpstreamHttpMethod = httpMethods != null ? new(httpMethods) : [HttpMethods.Get];
        r.UpstreamHost = upstreamHost;
        r.ServiceName = serviceName;
        r.LoadBalancerOptions = new()
        {
            Type = loadBalancerType ?? nameof(LeastConnection),
            Key = serviceName,
            Expiry = 60_000,
        };
        return r;
    }

    private async Task WhenIGetUrlOfRequestComingFromHost(string url, string requestHost, CookieHeaderValue cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(nameof(HttpRequestHeaders.Host), requestHost); // !
        if (cookie != null)
            request.Headers.Add("Cookie", cookie.ToString());
        response = await ocelotClient.ShouldNotBeNull().SendAsync(request, CancelMe);
    }

    private void ThenTheTokenIs(string token)
    {
        _receivedToken.ShouldBe(token);
    }

    private void WhenIAddAServiceBackIn(ServiceEntry serviceEntry)
    {
        _consulServices.Add(serviceEntry);
    }

    private void WhenIRemoveAService(ServiceEntry serviceEntry)
    {
        _consulServices.Remove(serviceEntry);
    }

    private void GivenIResetCounters()
    {
        Counters[0] = Counters[1] = 0;
        _counterConsul = 0;
    }

    private void GivenTheServicesAreRegisteredWithConsul(params ServiceEntry[] serviceEntries) => _consulServices.AddRange(serviceEntries);
    private void GivenTheServiceNodesAreRegisteredWithConsul(params Node[] nodes) => _consulNodes.AddRange(nodes);

    [GeneratedRegex("/v1/health/service/(?<serviceName>[^/]+)", RegexOptions.Singleline, RegexGlobal.DefaultMatchTimeoutMilliseconds)]
    private static partial Regex ServiceNameRegex();

    private void GivenThereIsAFakeConsulServiceDiscoveryProvider(string url)
        => _consulHandler.GivenThereIsAServiceRunningOn(url, MapServiceName);
    private async Task MapServiceName(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Consul-Token", out var values))
        {
            _receivedToken = values.First();
        }

        // Parse the request path to get the service name
        var pathMatch = ServiceNameRegex().Match(context.Request.Path.Value);
        if (pathMatch.Success)
        {
            //string json;
            //lock (ConsulCounterLocker)
            //{
            //_counterConsul++;
            int count = Interlocked.Increment(ref _counterConsul);

            // Use the parsed service name to filter the registered Consul services
            var serviceName = pathMatch.Groups["serviceName"].Value;
            var services = _consulServices.Where(x => x.Service.Service == serviceName).ToList();
            var json = JsonConvert.SerializeObject(services);

            //}
            context.Response.Headers.Append("Content-Type", "application/json");
            await context.Response.WriteAsync(json, context.RequestAborted);
            return;
        }

        if (context.Request.Path.Value == "/v1/catalog/nodes")
        {
            //_counterNodes++;
            int count = Interlocked.Increment(ref _counterNodes);
            var json = JsonConvert.SerializeObject(_consulNodes);
            context.Response.Headers.Append("Content-Type", "application/json");
            await context.Response.WriteAsync(json, context.RequestAborted);
        }
    }

    private void ThenConsulShouldHaveBeenCalledTimes(int expected) => _counterConsul.ShouldBe(expected);
    private void ThenConsulNodesShouldHaveBeenCalledTimes(int expected) => _counterNodes.ShouldBe(expected);
}
