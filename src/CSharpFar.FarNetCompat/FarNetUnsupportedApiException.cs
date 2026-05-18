namespace FarNet;

public sealed class FarNetUnsupportedApiException : NotSupportedException
{
    public FarNetUnsupportedApiException(string apiName)
        : base($"FarNet API '{apiName}' is not supported by CSharpFar FarNet compatibility v1.")
    {
        ApiName = apiName;
    }

    public string ApiName { get; }
}

public sealed class ModuleException : Exception
{
    public ModuleException()
    {
    }

    public ModuleException(string message)
        : base(message)
    {
    }

    public ModuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
