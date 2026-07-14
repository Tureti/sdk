using Microsoft.IO;
using Proton.Sdk.Cryptography;

namespace Proton.Drive.Sdk.Nodes.Upload;

internal readonly record struct ContentBlockEncryptionResult(
    RecyclableMemoryStream EncryptedContentStream,
    byte[] Sha256Digest,
    PgpArmoredMessage EncryptedSignature);
