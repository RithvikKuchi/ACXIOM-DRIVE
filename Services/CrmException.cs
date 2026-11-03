namespace AcxiomCRM.Services;

public class CrmException : Exception
{
    public int StatusCode { get; }
    public Dictionary<string, string[]> Errors { get; }
    public CrmException(string message, int statusCode = 400, Dictionary<string, string[]>? errors = null) : base(message)
    { StatusCode = statusCode; Errors = errors ?? new(); }
}
