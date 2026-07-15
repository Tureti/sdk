using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes;

namespace Proton.Drive.Sdk.Shares;

internal static class ShareOperations
{
    public static async ValueTask<ShareAndKey> GetShareAsync(ProtonDriveClient client, ShareId shareId, CancellationToken cancellationToken)
    {
        var response = await client.Api.Shares.GetShareAsync(shareId, cancellationToken).ConfigureAwait(false);

        if (response.MembershipAddressId is not { } membershipAddressId)
        {
            throw new InvalidOperationException($"Membership address ID is missing for share \"{shareId}\"");
        }

        var rootFolderId = new NodeUid(response.VolumeId, response.RootLinkId);

        var shareKey = await client.Cache.GetOrCreateShareKeyAsync(
            shareId,
            async ct =>
            {
                var (_, key) = await ShareCrypto.DecryptShareAsync(
                    client,
                    shareId,
                    response.Key,
                    response.Passphrase,
                    membershipAddressId,
                    rootFolderId,
                    response.Type,
                    ct).ConfigureAwait(false);

                return key;
            },
            cancellationToken).ConfigureAwait(false);

        var share = new Share(shareId, rootFolderId, membershipAddressId, response.Type);

        return new ShareAndKey(share, shareKey);
    }

    public static async ValueTask<List<Share>> GetSharesAsync(ProtonDriveClient client, ShareType? typeFilter, CancellationToken cancellationToken)
    {
        var response = await client.Api.Shares.GetSharesAsync(typeFilter, cancellationToken).ConfigureAwait(false);

        return response.Shares.Select(dto => new Share(dto.Id, new NodeUid(dto.VolumeId, dto.RootLinkId), default, dto.Type)).ToList();
    }

    public static async ValueTask<ShareAndKey> GetContextShareAsync(
        ProtonDriveClient client,
        NodeMetadata nodeMetadata,
        CancellationToken cancellationToken)
    {
        var contextRoot = await TraversalOperations.FindRootForNodeAsync(client, nodeMetadata, cancellationToken).ConfigureAwait(false);
        var contextShareId = contextRoot.MembershipShareId;

        if (!contextShareId.HasValue)
        {
            throw new InvalidOperationException("Node does not have a valid context share");
        }

        return await GetShareAsync(client, (ShareId)contextShareId, cancellationToken).ConfigureAwait(false);
    }
}
