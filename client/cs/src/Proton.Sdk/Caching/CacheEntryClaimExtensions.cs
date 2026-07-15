namespace Proton.Sdk.Caching;

public static class CacheEntryClaimExtensions
{
    /// <summary>
    /// Cancels the claim when <paramref name="exception"/> represents caller cancellation, otherwise fails the claim with <paramref name="exception"/>.
    /// </summary>
    public static void CancelOrFail(
        this ICacheEntryClaim claim,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            claim.Cancel();
        }
        else
        {
            claim.Fail(exception);
        }
    }
}
