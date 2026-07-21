using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Shares;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Api.Shares;

internal interface ISharesApiClient
{
    ValueTask<ShareResponseV2> GetMyFilesShareAsync(CancellationToken cancellationToken);
    ValueTask<ShareResponse> GetShareAsync(ShareId id, CancellationToken cancellationToken);
    ValueTask<ShareListResponse> GetSharesAsync(ShareType? typeFilter, CancellationToken cancellationToken);
    ValueTask<SharedWithMeResponse> GetSharedWithMeAsync(LinkId? anchorId, CancellationToken cancellationToken);
    ValueTask<SharedByMeResponse> GetSharedByMeAsync(VolumeId volumeId, LinkId? anchorId, CancellationToken cancellationToken);
    ValueTask RemoveMemberAsync(ShareId shareId, ShareMembershipId memberId, CancellationToken cancellationToken);
}
