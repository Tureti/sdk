using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Proton.Sdk.Api.Http;

public static class HttpResponseMessageExtensions
{
    public static async Task EnsureApiSuccessAsync<TFailure>(
        this HttpResponseMessage responseMessage,
        JsonTypeInfo<TFailure> failureTypeInfo,
        CancellationToken cancellationToken)
        where TFailure : ApiResponse
    {
        switch (responseMessage.StatusCode)
        {
            case HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict:
                {
                    var response = await ReadApiResponseAsync(responseMessage, failureTypeInfo, GetUnknownApiException, cancellationToken)
                        .ConfigureAwait(false);

                    throw new ProtonApiException<TFailure>(responseMessage.StatusCode, response);
                }

            case HttpStatusCode.BadRequest:
                {
                    var response = await ReadApiResponseAsync(
                        responseMessage,
                        ApiSerializerContext.Default.ApiResponse,
                        GetUnknownApiException,
                        cancellationToken).ConfigureAwait(false);

                    throw new ProtonApiException(responseMessage.StatusCode, response);
                }

            case HttpStatusCode.TooManyRequests:
                {
                    var retryAfter = GetRetryAfter(responseMessage);

                    var response = await ReadApiResponseAsync(
                        responseMessage,
                        ApiSerializerContext.Default.ApiResponse,
                        (rm, ex) => new TooManyRequestsException(rm.ReasonPhrase ?? "Too many requests", retryAfter, ex),
                        cancellationToken).ConfigureAwait(false);

                    throw new TooManyRequestsException(response, retryAfter);
                }

            default:
                responseMessage.EnsureSuccessStatusCode();
                break;
        }
    }

    private static async Task<TFailure> ReadApiResponseAsync<TFailure>(
        HttpResponseMessage responseMessage,
        JsonTypeInfo<TFailure> failureTypeInfo,
        Func<HttpResponseMessage, Exception?, Exception> getUnknownBodyException,
        CancellationToken cancellationToken)
    {
        if (responseMessage.Content.Headers.ContentType is not { MediaType: MediaTypeNames.Application.Json })
        {
            throw getUnknownBodyException.Invoke(responseMessage, null);
        }

        try
        {
            return await responseMessage.Content.ReadFromJsonAsync(failureTypeInfo, cancellationToken)
                .ConfigureAwait(false) ?? throw new JsonException("Failed to deserialize API response.");
        }
        catch (Exception ex)
        {
            throw getUnknownBodyException.Invoke(responseMessage, ex);
        }
    }

    private static Exception GetUnknownApiException(HttpResponseMessage responseMessage, Exception? innerException)
    {
        return new ProtonApiException(responseMessage.ReasonPhrase, (int)responseMessage.StatusCode, ApiResponseCodes.Unknown, innerException);
    }

    private static DateTime? GetRetryAfter(HttpResponseMessage responseMessage)
    {
        var retryAfter = responseMessage.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } offset)
        {
            return DateTime.UtcNow.Add(offset);
        }

        if (retryAfter.Date is { } date)
        {
            return date.UtcDateTime;
        }

        throw new SerializationException("Invalid Retry-After header");
    }
}
