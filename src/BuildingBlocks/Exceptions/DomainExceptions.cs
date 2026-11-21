namespace GreenCarWash.BuildingBlocks.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string resource, object id)
        : base($"{resource} '{id}' was not found.")
    {
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}

public class BadGatewayException : Exception
{
    public BadGatewayException(string message) : base(message)
    {
    }
}

public class GatewayTimeoutException : Exception
{
    public GatewayTimeoutException(string message) : base(message)
    {
    }
}

public class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message)
    {
    }
}
