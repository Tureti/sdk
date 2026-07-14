using Proton.Drive.Sdk.Api.Files;
using Proton.Drive.Sdk.Nodes.Upload.Verification;

namespace Proton.Drive.Sdk.Nodes.Upload;

internal interface IRevisionUploadBackend
{
    RevisionUid? RevisionUid { get; }

    bool IsSmallUpload { get; }

    IBlockVerifier BlockVerifier { get; }

    ValueTask<BlockUploadResult> UploadContentBlockAsync(
        int blockNumber,
        BlockUploadPlainData plainData,
        Action<long>? onProgress,
        CancellationToken cancellationToken);

    ValueTask<BlockUploadResult> UploadThumbnailBlockAsync(Thumbnail thumbnail, CancellationToken cancellationToken);

    ValueTask<UploadResult> CommitAsync(RevisionUpdateRequest request, ReadOnlyMemory<byte> sha1Digest, CancellationToken cancellationToken);

    ValueTask DeleteDraftIfNeededAsync();
}
