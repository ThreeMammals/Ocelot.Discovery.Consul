namespace Ocelot.Discovery.Consul;

public interface IConsulClientFactory
{
    IConsulClient Get(ConsulRegistryConfiguration config);
}
