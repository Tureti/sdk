using CommunityToolkit.HighPerformance;
using Proton.Cryptography.Pgp;

namespace Proton.Drive.Sdk.Nodes.Upload.Verification;

internal abstract class BlockVerifierBase : IBlockVerifier
{
    internal const int MaxPlainDataVerificationLength = 16;
    internal const int VerificationCodeLength = 32;

    private readonly PgpSessionKey _sessionKey;
    private readonly ReadOnlyMemory<byte> _verificationCode;

    protected BlockVerifierBase(PgpSessionKey sessionKey, ReadOnlyMemory<byte> verificationCode)
    {
        _sessionKey = sessionKey;
        _verificationCode = verificationCode;
    }

    public VerificationToken VerifyBlock(ReadOnlyMemory<byte> dataPacketPrefix, ReadOnlySpan<byte> plainDataPrefix)
    {
        try
        {
            var verificationLength = Math.Min(MaxPlainDataVerificationLength, plainDataPrefix.Length);
            using var decryptingStream = _sessionKey.OpenDecryptingStream(dataPacketPrefix.AsStream());

            Span<byte> decryptedDataPrefix = stackalloc byte[verificationLength];

            var numberOfBytesRead = decryptingStream.ReadAtLeast(
                decryptedDataPrefix,
                decryptedDataPrefix.Length,
                throwOnEndOfStream: false);

            if (numberOfBytesRead != verificationLength
                || !plainDataPrefix[..verificationLength].SequenceEqual(decryptedDataPrefix))
            {
                throw new SessionKeyAndDataPacketMismatchException("Mismatched plaintext verification");
            }
        }
        catch (Exception e) when (e is not SessionKeyAndDataPacketMismatchException)
        {
            throw new SessionKeyAndDataPacketMismatchException(e);
        }

        return VerificationToken.Create(_verificationCode.Span, dataPacketPrefix.Span);
    }

    protected static PgpSessionKey DecryptSessionKey(PgpPrivateKey nodeKey, ReadOnlyMemory<byte> contentKeyPacket)
    {
        try
        {
            return nodeKey.DecryptSessionKey(contentKeyPacket.Span);
        }
        catch (Exception e)
        {
            throw new NodeKeyAndSessionKeyMismatchException(e);
        }
    }
}
