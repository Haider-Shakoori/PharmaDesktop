using System.Net;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseApiException : Exception
{
    public LicenseApiException(
        string message,
        bool isRetryable,
        HttpStatusCode? statusCode = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
        ValidationErrors = validationErrors ??
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsRetryable { get; }

    public HttpStatusCode? StatusCode { get; }

    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }
}
