using Consul;
using Microsoft.AspNetCore.Http;
using Ocelot.Logging;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ocelot.Discovery.Consul.UnitTests;

public sealed class DefaultConsulServiceBuilderTests : UnitTest
{
    private DefaultConsulServiceBuilder sut;
    private readonly Mock<IHttpContextAccessor> contextAccessor;
    private readonly Mock<IConsulClientFactory> clientFactory;
    private readonly Mock<IOcelotLoggerFactory> loggerFactory;
    private readonly Mock<IOcelotLogger> logger;
    private ConsulRegistryConfiguration _configuration;

    public DefaultConsulServiceBuilderTests()
    {
        contextAccessor = new();
        clientFactory = new();
        clientFactory.Setup(x => x.Get(It.IsAny<ConsulRegistryConfiguration>()))
            .Returns(new ConsulClient());
        logger = new();
        loggerFactory = new();
        loggerFactory.Setup(x => x.CreateLogger<DefaultConsulServiceBuilder>())
            .Returns(logger.Object);
    }

    private void Arrange([CallerMemberName] string testName = null)
    {
        _configuration = new(null, null, 0, testName, null);
        var context = new DefaultHttpContext();
        context.Items.Add(nameof(ConsulRegistryConfiguration), _configuration);
        contextAccessor.SetupGet(x => x.HttpContext).Returns(context);
        sut = new DefaultConsulServiceBuilder(contextAccessor.Object, clientFactory.Object, loggerFactory.Object);
    }

    [Fact]
    public void Ctor_PrivateMembers_PropertiesAreInitialized()
    {
        Arrange();
        var propClient = sut.GetType().GetProperty("Client", BindingFlags.NonPublic | BindingFlags.Instance);
        var propLogger = sut.GetType().GetProperty("Logger", BindingFlags.NonPublic | BindingFlags.Instance);
        var propConfiguration = sut.GetType().GetProperty("Configuration", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(propClient);
        Assert.NotNull(propLogger);
        Assert.NotNull(propConfiguration);

        // Act
        var actualConfiguration = propConfiguration.GetValue(sut);
        var actualClient = propClient.GetValue(sut);
        var actualLogger = propLogger.GetValue(sut);

        // Assert
        Assert.NotNull(actualConfiguration);
        Assert.Equal(_configuration, actualConfiguration);
        Assert.NotNull(actualClient);
        Assert.NotNull(actualLogger);
    }

    private static Type Me { get; } = typeof(DefaultConsulServiceBuilder);
    private static MethodInfo GetNode { get; } = Me.GetMethod(nameof(GetNode), BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void GetNode_EntryBranch_ReturnsEntryNode()
    {
        Arrange();
        Node node = new() { Name = nameof(GetNode_EntryBranch_ReturnsEntryNode) };
        ServiceEntry entry = new() { Node = node };

        // Act
        var actual = GetNode.Invoke(sut, [entry, null]) as Node;

        // Assert
        Assert.NotNull(actual);
        Assert.Same(node, actual);
        Assert.Equal(node.Name, actual.Name);
    }

    [Fact]
    public void GetNode_NodesBranch_ReturnsNodeFromCollection()
    {
        Arrange();
        ServiceEntry entry = new()
        {
            Node = null,
            Service = new() { Address = TestName() },
        };
        Node[] nodes = null;

        // Act, Assert: nodes is null
        var actual = GetNode.Invoke(sut, [entry, nodes]) as Node;
        Assert.Null(actual);

        // Arrange, Act, Assert: nodes has items, happy path
        var node = new Node { Address = TestName() };
        nodes = [node];
        actual = GetNode.Invoke(sut, [entry, nodes]) as Node;
        Assert.NotNull(actual); Assert.Same(node, actual);
        Assert.Equal(entry.Service.Address, actual.Address);

        // Arrange, Act, Assert: nodes has items, some nulls in entry
        entry.Service.Address = null;
        actual = GetNode.Invoke(sut, [entry, nodes]) as Node;
        Assert.Null(actual);

        entry.Service = null;
        actual = GetNode.Invoke(sut, [entry, nodes]) as Node;
        Assert.Null(actual);

        entry = null;
        actual = GetNode.Invoke(sut, [entry, nodes]) as Node;
        Assert.Null(actual);
    }

    private static MethodInfo GetDownstreamHost { get; } = Me.GetMethod(nameof(GetDownstreamHost), BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void GetDownstreamHost_BothBranches_NameOrAddress()
    {
        Arrange();

        // Arrange, Act, Assert: node branch with a hostname
        ServiceEntry entry = new()
        {
            Service = new() { Address = TestName() },
        };
        var node = new Node { Name = "test1" };
        var actual = GetDownstreamHost.Invoke(sut, [entry, node]) as string;
        Assert.NotNull(actual); Assert.Equal("test1", actual);

        // Arrange, Act, Assert: node branch with a custom non-host name
        node = new Node { Name = "not a host name" };
        actual = GetDownstreamHost.Invoke(sut, [entry, node]) as string;
        Assert.NotNull(actual); Assert.Equal(TestName(), actual);

        // Arrange, Act, Assert: entry branch
        node = null;
        actual = GetDownstreamHost.Invoke(sut, [entry, node]) as string;
        Assert.NotNull(actual); Assert.Equal(TestName(), actual);
    }

    private static MethodInfo GetServiceVersion { get; } = Me.GetMethod(nameof(GetServiceVersion), BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void GetServiceVersion_TagsIsNull_EmptyString()
    {
        Arrange();

        // Arrange, Act, Assert: collection is null
        ServiceEntry entry = new()
        {
            Service = new() { Tags = null },
        };
        Node node = null;
        var actual = GetServiceVersion.Invoke(sut, [entry, node]) as string;
        Assert.Empty(actual);

        // Arrange, Act, Assert: collection has no version tag
        entry.Service.Tags = ["test"];
        actual = GetServiceVersion.Invoke(sut, [entry, node]) as string;
        Assert.Empty(actual);
    }

    [Fact]
    public void GetServiceVersion_HasTags_HappyPath()
    {
        Arrange();

        // Arrange
        var tags = new string[] { "test", "version-v2" };
        ServiceEntry entry = new()
        {
            Service = new() { Tags = tags },
        };
        Node node = null;

        // Act
        var actual = GetServiceVersion.Invoke(sut, [entry, node]) as string;

        // Assert
        Assert.Equal("v2", actual);
    }

    private static MethodInfo GetServiceTags { get; } = Me.GetMethod(nameof(GetServiceTags), BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void GetServiceTags_BothBranches()
    {
        Arrange();

        // Arrange, Act, Assert: collection is null
        ServiceEntry entry = new()
        {
            Service = new() { Tags = null },
        };
        Node node = null;
        var actual = GetServiceTags.Invoke(sut, [entry, node]) as IEnumerable<string>;
        Assert.NotNull(actual); Assert.Empty(actual);

        // Arrange, Act, Assert: happy path
        entry.Service.Tags = ["1", "2", "3"];
        actual = GetServiceTags.Invoke(sut, [entry, node]) as IEnumerable<string>;
        Assert.NotNull(actual); Assert.NotEmpty(actual);
        Assert.Equal(3, actual.Count());
        Assert.Contains("3", actual);
    }

    [Fact]
    public void IsValid_ValidAddressAndPort_ReturnsTrue()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Address = "localhost", Port = 80, Service = "my-service" },
        };

        var result = sut.IsValid(entry);

        Assert.True(result);
        logger.Verify(x => x.LogWarning(It.IsAny<Func<string>>()), Times.Never);
    }

    [Fact]
    public void IsValid_EmptyAddress_ReturnsFalseAndLogsWarning()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Address = string.Empty, Port = 80, Service = "svc" },
        };

        var result = sut.IsValid(entry);

        Assert.False(result);
        logger.Verify(x => x.LogWarning(It.IsAny<Func<string>>()), Times.Once);
    }

    [Fact]
    public void IsValid_AddressWithHttpPrefix_ReturnsFalse()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Address = "http://localhost", Port = 80, Service = "svc" },
        };

        var result = sut.IsValid(entry);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_AddressWithHttpsPrefix_ReturnsFalse()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Address = "https://localhost", Port = 443, Service = "svc" },
        };

        var result = sut.IsValid(entry);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_ZeroPort_ReturnsFalse()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Address = "localhost", Port = 0, Service = "svc" },
        };

        var result = sut.IsValid(entry);

        Assert.False(result);
    }

    [Fact]
    public void BuildServices_NullEntries_ThrowsArgumentNullException()
    {
        Arrange();

        Assert.Throws<ArgumentNullException>(() => sut.BuildServices(null, null));
    }

    [Fact]
    public void BuildServices_ValidEntries_ReturnsServiceList()
    {
        Arrange();
        var entries = new ServiceEntry[]
        {
            new()
            {
                Node = new Node { Name = "node1", Address = "10.0.0.1" },
                Service = new AgentService { Service = "svc", Address = "10.0.0.1", Port = 8080, ID = "id1" },
            },
        };

        var result = sut.BuildServices(entries, null).ToList();

        Assert.Single(result);
        Assert.Equal("svc", result[0].Name);
        Assert.Equal(8080, result[0].HostAndPort.DownstreamPort);
    }

    [Fact]
    public void BuildServices_EmptyEntries_ReturnsEmptyList()
    {
        Arrange();

        var result = sut.BuildServices([], null).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void BuildServices_InvalidEntry_SkipsEntry()
    {
        Arrange();
        var entries = new ServiceEntry[]
        {
            new() { Service = new AgentService { Address = string.Empty, Port = 80, Service = "svc" } },
        };

        var result = sut.BuildServices(entries, null).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void CreateService_ValidEntry_ReturnsService()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService
            {
                Service = "svc",
                Address = "localhost",
                Port = 8080,
                ID = "id1",
                Tags = ["version-v2"],
            },
        };

        var result = sut.CreateService(entry, null);

        Assert.NotNull(result);
        Assert.Equal("svc", result.Name);
        Assert.Equal("localhost", result.HostAndPort.DownstreamHost);
        Assert.Equal(8080, result.HostAndPort.DownstreamPort);
        Assert.Equal("id1", result.Id);
        Assert.Equal("v2", result.Version);
    }

    [Fact]
    public void CreateService_WithNode_UsesNodeName()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Service = "svc", Address = "10.0.0.1", Port = 8080, ID = "id1" },
        };
        var node = new Node { Name = "my-node", Address = "10.0.0.1" };

        var result = sut.CreateService(entry, node);

        Assert.NotNull(result);
        Assert.Equal("my-node", result.HostAndPort.DownstreamHost);
    }

    [Fact]
    public void CreateService_WithNonHostNodeName_UsesServiceAddress()
    {
        Arrange();
        var entry = new ServiceEntry
        {
            Service = new AgentService { Service = "svc", Address = "10.0.0.1", Port = 8080, ID = "id1" },
        };
        var node = new Node { Name = "not a host name", Address = "10.0.0.1" };

        var result = sut.CreateService(entry, node);

        Assert.NotNull(result);
        Assert.Equal("10.0.0.1", result.HostAndPort.DownstreamHost);
    }
}
