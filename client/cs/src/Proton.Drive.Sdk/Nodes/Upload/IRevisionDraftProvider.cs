namespace Proton.Drive.Sdk.Nodes.Upload;

internal interface IRevisionDraftProvider
{
    ValueTask<RevisionDraft> GetDraftAsync(
        long intendedUploadSize,
        IReadOnlyList<Thumbnail> thumbnails,
        bool contentCanSeek,
        bool allowSmallUpload,
        CancellationToken cancellationToken);
}
