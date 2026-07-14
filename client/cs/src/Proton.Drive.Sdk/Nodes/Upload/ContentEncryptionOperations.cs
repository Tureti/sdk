using System.Security.Cryptography;
using CommunityToolkit.HighPerformance;
using Microsoft.IO;
using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Cryptography;
using Proton.Drive.Sdk.Nodes.Upload.Verification;
using Proton.Sdk.Cryptography;

namespace Proton.Drive.Sdk.Nodes.Upload;

internal static class ContentEncryptionOperations
{
    internal static async ValueTask<(ContentBlockEncryptionResult Encryption, VerificationToken Token)> EncryptAndVerifyContentBlockAsync(
        RecyclableMemoryStreamManager memoryStreamManager,
        PgpPrivateKey fileKey,
        PgpSessionKey contentKey,
        PgpPrivateKey signingKey,
        BlockUploadPlainData plainData,
        PgpProfile pgpProfile,
        IBlockVerifier blockVerifier,
        int maxRetries,
        Func<bool, Task> onVerificationError,
        Action<int>? onRetry,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var integrityErrorEncountered = false;

        while (true)
        {
            attempt++;
            plainData.Stream.Seek(0, SeekOrigin.Begin);

            var encryptionResult = await EncryptContentBlockAsync(
                memoryStreamManager,
                fileKey,
                contentKey,
                signingKey,
                plainData.Stream,
                pgpProfile,
                cancellationToken).ConfigureAwait(false);

            try
            {
                var plainDataPrefixLength = (int)Math.Min(BlockVerifierBase.MaxPlainDataVerificationLength, plainData.Stream.Length);
                var verificationToken = blockVerifier.VerifyBlock(
                    encryptionResult.EncryptedContentStream.GetFirstBytes(PgpDefaults.AeadDecryptionMinimumInputLength),
                    plainData.PrefixForVerification.AsSpan()[..plainDataPrefixLength]);

                if (integrityErrorEncountered)
                {
                    await onVerificationError(true).ConfigureAwait(false);
                }

                return (encryptionResult, verificationToken);
            }
            catch (SessionKeyAndDataPacketMismatchException) when (attempt <= maxRetries)
            {
                integrityErrorEncountered = true;
                await encryptionResult.EncryptedContentStream.DisposeAsync().ConfigureAwait(false);
                onRetry?.Invoke(attempt);
            }
            catch (SessionKeyAndDataPacketMismatchException)
            {
                await encryptionResult.EncryptedContentStream.DisposeAsync().ConfigureAwait(false);
                await onVerificationError(false).ConfigureAwait(false);
                throw;
            }
        }
    }

    internal static async ValueTask<ThumbnailEncryptionResult> EncryptThumbnailAsync(
        RecyclableMemoryStreamManager memoryStreamManager,
        PgpSessionKey contentKey,
        PgpPrivateKey signingKey,
        PgpProfile pgpProfile,
        ReadOnlyMemory<byte> thumbnailContent,
        CancellationToken cancellationToken)
    {
        var encryptedThumbnailStream = memoryStreamManager.GetStream();

        try
        {
            var encryptingStream = contentKey.OpenEncryptingAndSigningReadStream(
                thumbnailContent.AsStream(),
                signingKey,
                profile: pgpProfile,
                aeadStreamingChunkLength: PgpDefaults.AeadStreamingChunkLength);

            await using (encryptingStream.ConfigureAwait(false))
            {
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var hashingStream = new HashingReadStream(encryptingStream, sha256, leaveOpen: true);

                await using (hashingStream.ConfigureAwait(false))
                {
                    await hashingStream.CopyToAsync(encryptedThumbnailStream, cancellationToken).ConfigureAwait(false);
                }

                return new ThumbnailEncryptionResult(encryptedThumbnailStream, sha256.GetCurrentHash());
            }
        }
        catch
        {
            await encryptedThumbnailStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async ValueTask<ContentBlockEncryptionResult> EncryptContentBlockAsync(
        RecyclableMemoryStreamManager memoryStreamManager,
        PgpPrivateKey fileKey,
        PgpSessionKey contentKey,
        PgpPrivateKey signingKey,
        Stream plaintextContent,
        PgpProfile pgpProfile,
        CancellationToken cancellationToken)
    {
        var encryptedContentStream = memoryStreamManager.GetStream();

        try
        {
            var signatureStream = memoryStreamManager.GetStream();

            await using (signatureStream.ConfigureAwait(false))
            {
                var encryptingStream = contentKey.OpenEncryptingAndSigningReadStream(
                    plaintextContent,
                    signatureStream,
                    signingKey,
                    profile: pgpProfile,
                    aeadStreamingChunkLength: PgpDefaults.AeadStreamingChunkLength);

                await using (encryptingStream.ConfigureAwait(false))
                {
                    using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var encryptedHashingStream = new HashingReadStream(encryptingStream, sha256, leaveOpen: true);

                    await using (encryptedHashingStream.ConfigureAwait(false))
                    {
                        await encryptedHashingStream.CopyToAsync(encryptedContentStream, cancellationToken).ConfigureAwait(false);
                    }

                    var encryptedSignature = PgpEncrypter.Encrypt(
                        signatureStream.GetBuffer().AsSpan()[..(int)signatureStream.Length],
                        fileKey);

                    return new ContentBlockEncryptionResult(
                        encryptedContentStream,
                        sha256.GetCurrentHash(),
                        new PgpArmoredMessage(encryptedSignature));
                }
            }
        }
        catch
        {
            await encryptedContentStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
