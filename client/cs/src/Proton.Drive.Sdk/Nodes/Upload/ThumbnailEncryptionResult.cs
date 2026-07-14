using Microsoft.IO;

namespace Proton.Drive.Sdk.Nodes.Upload;

internal readonly record struct ThumbnailEncryptionResult(
    RecyclableMemoryStream EncryptedThumbnailStream,
    byte[] Sha256Digest);
