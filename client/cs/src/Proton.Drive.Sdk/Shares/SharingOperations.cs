using System.Runtime.CompilerServices;
using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes;

namespace Proton.Drive.Sdk.Shares;

internal static class SharingOperations
{
    // The types of shared items exposed by the Drive client. Albums and photos are handled by the Photos client.
    private static readonly ShareTargetType[] DriveShareTargetTypes =
        [ShareTargetType.Folder, ShareTargetType.File, ShareTargetType.ProtonVendor];

    public static async IAsyncEnumerable<NodeUid> EnumerateSharedWithMeNodeUidsAsync(
        ProtonDriveClient client,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? anchorId = null;
        var mustTryMoreResults = true;

        while (mustTryMoreResults)
        {
            var response = await client.Api.Shares.GetSharedWithMeAsync(anchorId, cancellationToken).ConfigureAwait(false);

            foreach (var link in response.Links)
            {
                if (!DriveShareTargetTypes.Contains(link.ShareTargetType))
                {
                    continue;
                }

                yield return new NodeUid(link.VolumeId, link.LinkId);
            }

            anchorId = response.AnchorId;
            mustTryMoreResults = response.More && !string.IsNullOrEmpty(anchorId);
        }
    }

    public static async ValueTask LeaveSharedNodeAsync(ProtonDriveClient client, NodeUid nodeUid, CancellationToken cancellationToken)
    {
        var response = await client.Api.Links.GetDetailsAsync(nodeUid.VolumeId, [nodeUid.LinkId], cancellationToken).ConfigureAwait(false);

        var linkDetails = response.Links.FirstOrDefault(link => link.Link.Id == nodeUid.LinkId);

        if (linkDetails?.Membership is not { } membership)
        {
            throw new ValidationException("You can leave only an item that is shared with you");
        }

        await client.Api.Shares.RemoveMemberAsync(membership.ShareId, membership.MembershipId, cancellationToken).ConfigureAwait(false);
    }
}
