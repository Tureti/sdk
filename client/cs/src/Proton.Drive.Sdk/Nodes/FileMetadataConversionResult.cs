using Proton.Drive.Sdk.Telemetry;

namespace Proton.Drive.Sdk.Nodes;

internal readonly record struct FileMetadataConversionResult(
    FileMetadata Metadata,
    IReadOnlyDictionary<EncryptedField, ProtonDriveError> FailedDecryptionFields);

internal readonly record struct FolderMetadataConversionResult(
    FolderMetadata Metadata,
    IReadOnlyDictionary<EncryptedField, ProtonDriveError> FailedDecryptionFields);

internal readonly record struct NodeMetadataConversionResult(
    NodeMetadata Metadata,
    IReadOnlyDictionary<EncryptedField, ProtonDriveError> FailedDecryptionFields)
{
    public static NodeMetadataConversionResult FromFile(FileMetadataConversionResult result) =>
        new(NodeMetadata.FromFile(result.Metadata), result.FailedDecryptionFields);

    public static NodeMetadataConversionResult FromFolder(FolderMetadataConversionResult result) =>
        new(NodeMetadata.FromFolder(result.Metadata), result.FailedDecryptionFields);
}
