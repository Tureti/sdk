namespace Proton.Drive.Sdk.Account;

public sealed record ProtonSessionOptions : ProtonClientOptions
{
    public Uri? AccountBaseUrl { get; set; }
}
