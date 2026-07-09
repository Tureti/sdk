using Proton.Drive.Sdk.Nodes;

namespace Proton.Drive.Sdk.Shares;

internal static class SharingOperations
{
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
