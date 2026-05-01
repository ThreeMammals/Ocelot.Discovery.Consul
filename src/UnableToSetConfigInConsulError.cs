using Microsoft.AspNetCore.Http;
using Ocelot.Errors;

namespace Ocelot.Discovery.Consul;

public class UnableToSetConfigInConsulError : Error
{
    public UnableToSetConfigInConsulError(string s)
        : base(s, OcelotErrorCode.UnknownError, StatusCodes.Status404NotFound)
    { }
}
