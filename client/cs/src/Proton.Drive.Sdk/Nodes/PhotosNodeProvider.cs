using Proton.Drive.Sdk.Api;
using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Nodes;

internal sealed class PhotosNodeProvider(IDriveApiClients api) : DriveNodeProvider(api)
{
    protected override ValueTask<LinkDetailsResponse> GetLinkDetailsAsync(
        VolumeId volumeId,
        IEnumerable<LinkId> linkIds,
        CancellationToken cancellationToken)
    {
        return Api.Photos.GetDetailsAsync(volumeId, linkIds, cancellationToken);
    }
}
