using System.Security.Cryptography;
using System.Text;
using Proton.Sdk.Cryptography;

namespace Proton.Sdk.Caching;

public sealed class EncryptedCacheRepository(ICacheRepository inner, byte[] encryptionKey) : ICacheRepository
{
    private const int IvByteCount = 12;
    private const int SaltByteCount = 16;
    private const int TagByteCount = 16;
    private const int KeyByteCount = 32;

    private readonly ICacheRepository _inner = inner;
    private readonly byte[] _encryptionKey = encryptionKey;

    private static byte[] CacheEncryptionContext => "Drive.EncryptedCacheRepository"u8.ToArray();

    public ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        var encryptedValue = Encrypt(key, value);

        return _inner.SetAsync(key, encryptedValue, cancellationToken);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        return _inner.RemoveAsync(key, cancellationToken);
    }

    public ValueTask ClearAsync()
    {
        return _inner.ClearAsync();
    }

    public async ValueTask<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        var encryptedValue = await _inner.TryGetAsync(key, cancellationToken).ConfigureAwait(false);

        try
        {
            return encryptedValue is not null ? Decrypt(key, encryptedValue) : null;
        }
        catch (AuthenticationTagMismatchException)
        {
            // If the tag is invalid, we assume either the cache has been tampered with or the
            // encryption key has changed. Clear the cache and behave as if we had no value in cache.
            await _inner.ClearAsync().ConfigureAwait(false);
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        return _inner.DisposeAsync();
    }

    private static byte[] Concatenate(byte[] a1, byte[] a2)
    {
        var stream = new MemoryStream(
            new byte[a1.Length + a2.Length],
            0,
            a1.Length + a2.Length,
            true,
            true);

        stream.Write(a1, 0, a1.Length);
        stream.Write(a2, 0, a2.Length);

        return stream.ToArray();
    }

    // TODO: use stack allocation when possible
    private byte[] Encrypt(string entryKey, ReadOnlyMemory<byte> plaintext)
    {
        var salt = CryptoSecureNumberGenerator.GetBytes(SaltByteCount);

        Span<byte> derivedMaterial = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _encryptionKey,
            KeyByteCount + IvByteCount,
            salt,
            Concatenate(CacheEncryptionContext, Encoding.UTF8.GetBytes(entryKey)));

        var derivedKey = derivedMaterial[..KeyByteCount];
        var iv = derivedMaterial[KeyByteCount..];
        Span<byte> ciphertext = stackalloc byte[plaintext.Length];
        Span<byte> tag = stackalloc byte[TagByteCount];

        using var aesGcm = new AesGcm(derivedKey, TagByteCount);
        aesGcm.Encrypt(iv, plaintext.Span, ciphertext, tag);

        // Format: [salt][ciphertext][tag]
        var result = new byte[SaltByteCount + plaintext.Length + TagByteCount];

        salt.CopyTo(result.AsSpan());
        ciphertext.CopyTo(result.AsSpan(SaltByteCount));
        tag.CopyTo(result.AsSpan(SaltByteCount + plaintext.Length));

        return result;
    }

    // TODO: use stack allocation when possible
    private byte[] Decrypt(string entryKey, byte[] encrypted)
    {
        // Validate minimum length: salt + tag
        if (encrypted.Length < SaltByteCount + TagByteCount)
        {
            throw new InvalidOperationException("Invalid encrypted data format");
        }

        var salt = encrypted[..SaltByteCount];
        var ciphertext = encrypted[SaltByteCount..^TagByteCount];
        var tag = encrypted[^TagByteCount..];

        Span<byte> derivedMaterial = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _encryptionKey,
            KeyByteCount + IvByteCount,
            salt,
            Concatenate(CacheEncryptionContext, Encoding.UTF8.GetBytes(entryKey)));

        var derivedKey = derivedMaterial[..KeyByteCount];
        var iv = derivedMaterial[KeyByteCount..];
        var plaintextBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(derivedKey, TagByteCount);
        aesGcm.Decrypt(iv, ciphertext, tag, plaintextBytes);

        return plaintextBytes;
    }
}
