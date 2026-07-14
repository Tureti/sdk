using Proton.Cryptography.Pgp;

namespace Proton.Drive.Sdk.Nodes.Upload.Verification;

internal sealed class SmallFileUploadBlockVerifier : BlockVerifierBase
{
    public SmallFileUploadBlockVerifier(PgpPrivateKey nodeKey, ReadOnlyMemory<byte> contentKeyPacket)
        : base(
            DecryptSessionKey(nodeKey, contentKeyPacket),
            contentKeyPacket[^VerificationCodeLength..])
    {
    }
}
