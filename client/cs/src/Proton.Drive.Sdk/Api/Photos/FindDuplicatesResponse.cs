namespace Proton.Drive.Sdk.Api.Photos;

internal sealed class FindDuplicatesResponse
{
    public required IReadOnlyList<FoundDuplicateDto> DuplicateHashes { get; init; }
}
