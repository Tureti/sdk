using System.Net;

namespace Proton.Sdk.Api;

public class TooManyRequestsException : ProtonApiException
{
    public TooManyRequestsException()
    {
    }

    public TooManyRequestsException(string message)
        : base(message)
    {
    }

    public TooManyRequestsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TooManyRequestsException(string message, DateTime? retryAfter, Exception? innerException = null)
        : base(message, (int)HttpStatusCode.TooManyRequests, ApiResponseCodes.Unknown, innerException)
    {
        RetryAfter = retryAfter;
    }

    public TooManyRequestsException(ApiResponse response, DateTime? retryAfter = null)
        : base(HttpStatusCode.TooManyRequests, response)
    {
        RetryAfter = retryAfter;
    }

    public DateTime? RetryAfter { get; }
}
