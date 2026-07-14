using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Api.BlockVerification;

namespace Proton.Drive.Sdk.Nodes.Upload.Verification;

internal sealed class BlockVerifier : BlockVerifierBase
{
    private BlockVerifier(PgpSessionKey sessionKey, ReadOnlyMemory<byte> verificationCode)
        : base(sessionKey, verificationCode)
    {
    }

    public static async ValueTask<BlockVerifier> CreateAsync(
        IBlockVerificationApiClient apiClient,
        RevisionUid revisionUid,
        PgpPrivateKey key,
        CancellationToken cancellationToken)
    {
        var verificationInput =
            await apiClient.GetVerificationInputAsync(revisionUid.NodeUid.VolumeId, revisionUid.NodeUid.LinkId, revisionUid.RevisionId, cancellationToken)
                .ConfigureAwait(false);

        var sessionKey = DecryptSessionKey(key, verificationInput.ContentKeyPacket);

        return new BlockVerifier(sessionKey, verificationInput.VerificationCode);
    }
}
