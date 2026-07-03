using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Api.Files;
using Proton.Drive.Sdk.Api.Folders;
using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Api.Photos;
using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes.Cryptography;
using Proton.Drive.Sdk.Shares;
using Proton.Drive.Sdk.Telemetry;
using Proton.Drive.Sdk.Volumes;
using Proton.Sdk;

namespace Proton.Drive.Sdk.Nodes;

internal static class DtoToMetadataConverter
{
    public static async Task<NodeMetadata> ConvertDtoToNodeMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        ShareAndKey? knownShareAndKey,
        CancellationToken cancellationToken)
    {
        var entryPointKey = linkDetailsDto.Link.ParentId is not null || linkDetailsDto.Photo is not { AlbumInclusions: { Count: > 0 } albumInclusions }
            ? await GetEntryPointKeyOrThrowAsync(
                client,
                volumeId,
                linkDetailsDto.Link.ParentId,
                knownShareAndKey,
                linkDetailsDto.Sharing?.ShareId,
                cancellationToken).ConfigureAwait(false)
            : await GetAlbumEntryPointKeyOrThrowAsync(client, volumeId, linkDetailsDto, knownShareAndKey, albumInclusions, cancellationToken)
                .ConfigureAwait(false);

        return await ConvertDtoToNodeMetadataAsync(
            client,
            volumeId,
            linkDetailsDto,
            entryPointKey,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<NodeMetadata> ConvertDtoToNodeMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        PgpPrivateKey parentKey,
        CancellationToken cancellationToken)
    {
        var linkType = linkDetailsDto.Link.Type;

        var nodeMetadata = linkType switch
        {
            LinkType.Folder =>
                NodeMetadata.FromFolder(await ConvertDtoToFolderMetadataAsync(
                    client,
                    volumeId,
                    linkDetailsDto,
                    parentKey,
                    cancellationToken).ConfigureAwait(false)),

            LinkType.File =>
                NodeMetadata.FromFile(await ConvertDtoToFileMetadataAsync(
                    client,
                    volumeId,
                    linkDetailsDto,
                    parentKey,
                    cancellationToken).ConfigureAwait(false)),

            LinkType.Album =>
                NodeMetadata.FromFolder(await ConvertDtoToAlbumMetadataAsync(
                    client,
                    volumeId,
                    linkDetailsDto,
                    parentKey,
                    cancellationToken).ConfigureAwait(false)),

            // FIXME: handle other existing node types, and determine a way for forward compatibility or degraded result in case a new node type is introduced
            _ => throw new NotSupportedException($"Link type {linkType} is not supported."),
        };

        return nodeMetadata;
    }

    public static async ValueTask<FolderMetadata> ConvertDtoToFolderMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        PgpPrivateKey parentKey,
        CancellationToken cancellationToken)
    {
        if (linkDetailsDto.Folder is null)
        {
            throw new InvalidOperationException("Node is a folder, but folder properties are missing");
        }

        return await ConvertDtoToFolderMetadataAsync(
            client,
            volumeId,
            linkDetailsDto,
            linkDetailsDto.Folder,
            parentKey,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<FolderMetadata> ConvertDtoToAlbumMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        PgpPrivateKey parentKey,
        CancellationToken cancellationToken)
    {
        if (linkDetailsDto.Album is null)
        {
            throw new InvalidOperationException("Node is an album, but album properties are missing");
        }

        return await ConvertDtoToFolderMetadataAsync(
            client,
            volumeId,
            linkDetailsDto,
            linkDetailsDto.Album,
            parentKey,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<FileMetadata> ConvertDtoToFileMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        PgpPrivateKey parentKey,
        CancellationToken cancellationToken)
    {
        var linkDto = linkDetailsDto.Link;
        var fileDto = linkDetailsDto.File ?? linkDetailsDto.Photo;
        var membershipDto = linkDetailsDto.Membership;

        if (fileDto is null)
        {
            // FIXME: handle missing file information with degraded node
            throw new InvalidOperationException("Node is a file, but file properties are missing");
        }

        if (linkDto.State is LinkState.Draft)
        {
            // We don't currently expect draft nodes
            throw new NotSupportedException("Draft nodes are not supported");
        }

        if (fileDto.ActiveRevision is not { } activeRevisionDto)
        {
            // FIXME: handle missing revision information with degraded node
            throw new InvalidOperationException("Node is a non-draft file, but active revision properties are missing");
        }

        var uid = new NodeUid(volumeId, linkDto.Id);
        var parentUid = linkDto.ParentId is not null ? (NodeUid?)new NodeUid(uid.VolumeId, linkDto.ParentId.Value) : null;

        var decryptionResult = await NodeCrypto.DecryptFileAsync(client.Account, linkDto, fileDto, activeRevisionDto, parentKey, cancellationToken)
            .ConfigureAwait(false);

        var nodeKeyIsValid = decryptionResult.Link.NodeKey.TryGetValue(out var nodeKey);
        var passphraseIsValid = decryptionResult.Link.Passphrase.TryGetValue(out var passphraseOutput);
        var extendedAttributesIsValid = decryptionResult.ExtendedAttributes.TryGetValue(out var extendedAttributesOutput);
        var contentKeyIsValid = decryptionResult.ContentKey.TryGetValue(out var contentKeyOutput);

        var thumbnails = activeRevisionDto.Thumbnails.Select(dto => new ThumbnailHeader(dto.Id, (ThumbnailType)dto.Type)).ToList().AsReadOnly();

        var extendedAttributes = extendedAttributesOutput.Data;
        var additionalMetadata = extendedAttributes?.AdditionalMetadata?.Select(x => new AdditionalMetadataProperty(x.Key, x.Value)).ToList().AsReadOnly();
        var modificationTimeResult = extendedAttributes?.Common?.ModificationTime;
        var modificationTimeIsValid = modificationTimeResult?.IsSuccess ?? true;

        if (!NodeOperations.ValidateName(decryptionResult.Link.Name, out var nameOutput, out var nameResult, out var nameSessionKey)
            || !nodeKeyIsValid
            || !passphraseIsValid
            || !extendedAttributesIsValid
            || !contentKeyIsValid
            || !modificationTimeIsValid)
        {
            var (partialFileMetadata, failedDecryptionFields) = CreatePartialFileMetadata(
                linkDetailsDto,
                decryptionResult,
                nameResult,
                uid,
                activeRevisionDto,
                extendedAttributes,
                modificationTimeResult,
                thumbnails,
                additionalMetadata,
                parentUid,
                linkDto,
                fileDto,
                nameSessionKey,
                membershipDto);

            await client.Cache.SetNodeOperationDataAsync(uid, partialFileMetadata.OperationData, cancellationToken).ConfigureAwait(false);

            await TelemetryRecorder.TryRecordDecryptionErrorAsync(
                client,
                partialFileMetadata.Node,
                failedDecryptionFields,
                cancellationToken).ConfigureAwait(false);

            return partialFileMetadata;
        }

        var operationData = new FileOperationData
        {
            ParentUid = parentUid,
            Key = nodeKey,
            PassphraseSessionKey = passphraseOutput.SessionKey,
            NameSessionKey = nameSessionKey.Value,
            ContentKey = contentKeyOutput.Data,
            PassphraseForAnonymousMove = decryptionResult.Link.NodeAuthorshipClaim.Author == Author.Anonymous
                ? passphraseOutput.Data
                : (ReadOnlyMemory<byte>?)null,
        };

        var nodeAuthor = decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(passphraseOutput.AuthorshipVerificationFailure);
        var nameAuthor = decryptionResult.Link.NameAuthorshipClaim.ToAuthorshipResult(nameOutput.Value.AuthorshipVerificationFailure);
        var contentAuthor = decryptionResult.ContentAuthorshipClaim.ToAuthorshipResult(contentKeyOutput.AuthorshipVerificationFailure);

        var activeRevision = new Revision
        {
            Uid = new RevisionUid(uid, activeRevisionDto.Id),
            CreationTime = activeRevisionDto.CreationTime,
            SizeOnCloudStorage = activeRevisionDto.StorageQuotaConsumption,
            ClaimedSize = extendedAttributes?.Common?.Size,
            ClaimedModificationTime = modificationTimeResult?.GetValueOrDefault(),
            ClaimedDigests =
                new FileContentDigests
                {
                    Sha1 = extendedAttributes?.Common?.Digests?.Sha1,
                    Sha1Verified = fileDto.ActiveRevision.ChecksumVerified ?? false,
                },
            Thumbnails = thumbnails.AsReadOnly(),
            AdditionalClaimedMetadata = additionalMetadata,
            ContentAuthor = contentAuthor,
        };

        var ownedBy = MapOwnedBy(linkDto.OwnedBy);
        var node = linkDetailsDto.Photo is not null
            ? new PhotoNode
            {
                Uid = uid,
                ParentUid = parentUid,
                Name = nameOutput.Value.Data,
                NameAuthor = nameAuthor,
                KeyAuthor = nodeAuthor,
                CreationTime = linkDto.CreationTime,
                TrashTime = linkDto.TrashTime,
                MediaType = fileDto.MediaType,
                ActiveRevision = activeRevision,
                TotalSizeOnCloudStorage = fileDto.TotalSizeOnStorage,
                CaptureTime = linkDetailsDto.Photo.CaptureTime,
                AlbumUids = linkDetailsDto.Photo.AlbumInclusions.Select(a => new NodeUid(uid.VolumeId, a.Id)).ToList(),
                OwnedBy = ownedBy,
                Errors = [],
            }
            : new FileNode
            {
                Uid = uid,
                ParentUid = parentUid,
                Name = nameOutput.Value.Data,
                NameAuthor = nameAuthor,
                KeyAuthor = nodeAuthor,
                CreationTime = linkDto.CreationTime,
                TrashTime = linkDto.TrashTime,
                MediaType = fileDto.MediaType,
                ActiveRevision = activeRevision,
                TotalSizeOnCloudStorage = fileDto.TotalSizeOnStorage,
                OwnedBy = ownedBy,
                Errors = [],
            };

        await client.Cache.SetNodeOperationDataAsync(uid, operationData, cancellationToken).ConfigureAwait(false);

        return new FileMetadata(node, operationData, membershipDto?.ShareId, linkDto.NameHashDigest);
    }

    private static (FileMetadata Metadata, Dictionary<EncryptedField, ProtonDriveError> FailedDecryptionFields) CreatePartialFileMetadata(
        LinkDetailsDto linkDetailsDto,
        FileDecryptionResult decryptionResult,
        Result<string, ProtonDriveError> nameResult,
        NodeUid uid,
        ActiveRevisionDto activeRevisionDto,
        ExtendedAttributes? extendedAttributes,
        Result<DateTime, ProtonDriveError>? modificationTimeResult,
        ReadOnlyCollection<ThumbnailHeader> thumbnails,
        ReadOnlyCollection<AdditionalMetadataProperty>? additionalMetadata,
        NodeUid? parentUid,
        LinkDto linkDto,
        FileDto fileDto,
        PgpSessionKey? nameSessionKey,
        ShareMembershipSummaryDto? membershipDto)
    {
        Dictionary<EncryptedField, ProtonDriveError> failedDecryptionFields = [];
        List<ProtonDriveError> nodeErrors = [];

        if (decryptionResult.Link.Passphrase.TryGetError(out var passphraseError))
        {
            nodeErrors.Add(passphraseError);

            if (passphraseError is DecryptionError)
            {
                failedDecryptionFields.Add(EncryptedField.NodeKey, passphraseError);
            }
        }
        else if (decryptionResult.Link.NodeKey.TryGetError(out var nodeKeyError))
        {
            nodeErrors.Add(nodeKeyError);

            if (nodeKeyError is DecryptionError)
            {
                failedDecryptionFields.Add(EncryptedField.NodeKey, nodeKeyError);
            }
        }
        else if (decryptionResult.ContentKey.TryGetError(out var contentKeyError))
        {
            failedDecryptionFields.Add(EncryptedField.NodeContentKey, contentKeyError);
        }

        if (nameResult.TryGetError(out var nameError))
        {
            failedDecryptionFields.Add(EncryptedField.NodeName, nameError);
        }

        if (modificationTimeResult?.TryGetError(out var modificationTimeError) == true)
        {
            nodeErrors.Add(new ExtendedAttributesDeserializationError("Failed to deserialize modification time", modificationTimeError));
        }

        if (decryptionResult.ExtendedAttributes.TryGetError(out var extendedAttributesError))
        {
            nodeErrors.Add(extendedAttributesError);

            if (extendedAttributesError is DecryptionError)
            {
                failedDecryptionFields.Add(EncryptedField.NodeExtendedAttributes, extendedAttributesError);
            }
        }

        var nodeAuthor = decryptionResult.Link.Passphrase.Merge(
            x => decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            error => new SignatureVerificationError(decryptionResult.Link.NodeAuthorshipClaim.Author, "Passphrase decryption failed", error));

        var nameAuthor = decryptionResult.Link.Name.Merge(
            x => decryptionResult.Link.NameAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            error => new SignatureVerificationError(decryptionResult.Link.NameAuthorshipClaim.Author, "Name decryption failed", error));

        var contentAuthor = decryptionResult.ContentKey.Merge(
            x => decryptionResult.ContentAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            error => new SignatureVerificationError(decryptionResult.ContentAuthorshipClaim.Author, "Content key decryption failed", error));

        var partialRevision = new Revision
        {
            Uid = new RevisionUid(uid, activeRevisionDto.Id),
            CreationTime = activeRevisionDto.CreationTime,
            SizeOnCloudStorage = activeRevisionDto.StorageQuotaConsumption,
            ClaimedSize = extendedAttributes?.Common?.Size,
            ClaimedModificationTime = modificationTimeResult?.GetValueOrDefault(),
            ClaimedDigests = new FileContentDigests { Sha1 = extendedAttributes?.Common?.Digests?.Sha1 },
            Thumbnails = thumbnails.AsReadOnly(),
            AdditionalClaimedMetadata = additionalMetadata,
            ContentAuthor = contentAuthor,
        };

        var ownedBy = MapOwnedBy(linkDto.OwnedBy);
        var partialNode = linkDetailsDto.Photo is not null
            ? new PhotoNode
            {
                Uid = uid,
                ParentUid = parentUid,
                Name = nameResult,
                NameAuthor = nameAuthor,
                CreationTime = linkDto.CreationTime,
                TrashTime = linkDto.TrashTime,
                KeyAuthor = nodeAuthor,
                MediaType = fileDto.MediaType,
                ActiveRevision = partialRevision,
                TotalSizeOnCloudStorage = fileDto.TotalSizeOnStorage,
                Errors = nodeErrors,
                CaptureTime = linkDetailsDto.Photo.CaptureTime,
                AlbumUids = linkDetailsDto.Photo.AlbumInclusions.Select(a => new NodeUid(uid.VolumeId, a.Id)).ToList(),
                OwnedBy = ownedBy,
            }
            : new FileNode
            {
                Uid = uid,
                ParentUid = parentUid,
                Name = nameResult,
                NameAuthor = nameAuthor,
                CreationTime = linkDto.CreationTime,
                TrashTime = linkDto.TrashTime,
                KeyAuthor = nodeAuthor,
                MediaType = fileDto.MediaType,
                ActiveRevision = partialRevision,
                TotalSizeOnCloudStorage = fileDto.TotalSizeOnStorage,
                Errors = nodeErrors,
                OwnedBy = ownedBy,
            };

        var partialSecrets = new FileOperationData
        {
            ParentUid = parentUid,
            Key = decryptionResult.Link.NodeKey.Merge(x => (PgpPrivateKey?)x, _ => null),
            PassphraseSessionKey = decryptionResult.Link.Passphrase.Merge(x => (PgpSessionKey?)x.SessionKey, _ => null),
            NameSessionKey = nameSessionKey,
            ContentKey = decryptionResult.ContentKey.Merge(x => (PgpSessionKey?)x.Data, _ => null),
        };

        return (new FileMetadata(partialNode, partialSecrets, membershipDto?.ShareId, linkDto.NameHashDigest), failedDecryptionFields);
    }

    private static async ValueTask<FolderMetadata> ConvertDtoToFolderMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        FolderDto folderDto,
        PgpPrivateKey parentKey,
        CancellationToken cancellationToken)
    {
        var linkDto = linkDetailsDto.Link;
        var membershipDto = linkDetailsDto.Membership;

        var uid = new NodeUid(volumeId, linkDto.Id);
        var parentUid = linkDto.ParentId is not null ? (NodeUid?)new NodeUid(uid.VolumeId, linkDto.ParentId.Value) : null;

        var decryptionResult = await NodeCrypto.DecryptFolderAsync(client.Account, linkDto, folderDto.HashKey, parentKey, cancellationToken)
            .ConfigureAwait(false);

        var nodeKeyIsValid = decryptionResult.Link.NodeKey.TryGetValue(out var nodeKey);
        var passphraseIsValid = decryptionResult.Link.Passphrase.TryGetValue(out var passphraseOutput);
        var hashKeyIsValid = decryptionResult.HashKey.TryGetValue(out var hashKeyOutput);

        if (!NodeOperations.ValidateName(decryptionResult.Link.Name, out var nameOutput, out var nameResult, out var nameSessionKey)
            || !passphraseIsValid
            || !nodeKeyIsValid
            || !hashKeyIsValid)
        {
            var (partialFolderMetadata, failedDecryptionFields) = CreatePartialFolderMetadata(
                decryptionResult,
                nameResult,
                uid,
                parentUid,
                linkDto,
                nameSessionKey,
                membershipDto);

            await client.Cache.SetNodeOperationDataAsync(uid, partialFolderMetadata.OperationData, cancellationToken).ConfigureAwait(false);

            await TelemetryRecorder.TryRecordDecryptionErrorAsync(
                client,
                partialFolderMetadata.Node,
                failedDecryptionFields,
                cancellationToken).ConfigureAwait(false);

            return partialFolderMetadata;
        }

        var operationData = new FolderOperationData
        {
            ParentUid = parentUid,
            Key = nodeKey,
            PassphraseSessionKey = passphraseOutput.SessionKey,
            NameSessionKey = nameSessionKey.Value,
            HashKey = hashKeyOutput.Data,
            PassphraseForAnonymousMove = decryptionResult.Link.NodeAuthorshipClaim.Author == Author.Anonymous ? passphraseOutput.Data : null,
        };

        var nodeAuthorFromPassphrase = decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(passphraseOutput.AuthorshipVerificationFailure);
        var nodeAuthorFromHashKey = decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(hashKeyOutput.AuthorshipVerificationFailure);

        var nodeAuthor = nodeAuthorFromHashKey.IsFailure ? nodeAuthorFromHashKey : nodeAuthorFromPassphrase;

        var nameAuthor = decryptionResult.Link.NameAuthorshipClaim.ToAuthorshipResult(nameOutput.Value.AuthorshipVerificationFailure);

        var node = new FolderNode
        {
            Uid = uid,
            ParentUid = parentUid,
            Name = nameOutput.Value.Data,
            NameAuthor = nameAuthor,
            KeyAuthor = nodeAuthor,
            CreationTime = linkDto.CreationTime,
            TrashTime = linkDto.TrashTime,
            OwnedBy = MapOwnedBy(linkDto.OwnedBy),
            Errors = [],
        };

        await client.Cache.SetNodeOperationDataAsync(uid, operationData, cancellationToken).ConfigureAwait(false);

        return new FolderMetadata(node, operationData, membershipDto?.ShareId, linkDto.NameHashDigest);
    }

    private static (FolderMetadata Metadata, Dictionary<EncryptedField, ProtonDriveError> FailedDecryptionFields) CreatePartialFolderMetadata(
        FolderDecryptionResult decryptionResult,
        Result<string, ProtonDriveError> nameResult,
        NodeUid uid,
        NodeUid? parentUid,
        LinkDto linkDto,
        PgpSessionKey? nameSessionKey,
        ShareMembershipSummaryDto? membershipDto)
    {
        Dictionary<EncryptedField, ProtonDriveError> failedDecryptionFields = [];
        List<ProtonDriveError> nodeKeyAndHashKeyErrors = [];

        if (decryptionResult.Link.Passphrase.TryGetError(out var passphraseError))
        {
            nodeKeyAndHashKeyErrors.Add(passphraseError);

            if (passphraseError is DecryptionError)
            {
                failedDecryptionFields.Add(EncryptedField.NodeKey, passphraseError);
            }
        }
        else if (decryptionResult.Link.NodeKey.TryGetError(out var nodeKeyError))
        {
            nodeKeyAndHashKeyErrors.Add(nodeKeyError);

            if (nodeKeyError is DecryptionError)
            {
                failedDecryptionFields.Add(EncryptedField.NodeKey, nodeKeyError);
            }
        }
        else if (decryptionResult.HashKey.TryGetError(out var hashKeyError))
        {
            nodeKeyAndHashKeyErrors.Add(hashKeyError);

            failedDecryptionFields.Add(EncryptedField.NodeHashKey, hashKeyError);
        }

        if (nameResult.TryGetError(out var nameError) && nameError is DecryptionError)
        {
            failedDecryptionFields.Add(EncryptedField.NodeName, nameError);
        }

        var nodeAuthorFromPassphrase = decryptionResult.Link.Passphrase.Merge(
            x => decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            _ => new SignatureVerificationError(decryptionResult.Link.NodeAuthorshipClaim.Author, "Passphrase decryption failed"));

        var nodeAuthorFromHashKey = decryptionResult.HashKey.Merge(
            x => decryptionResult.Link.NodeAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            _ => new SignatureVerificationError(decryptionResult.Link.NodeAuthorshipClaim.Author, "Hash key decryption failed"));

        var nodeAuthor = nodeAuthorFromHashKey.IsFailure ? nodeAuthorFromHashKey : nodeAuthorFromPassphrase;

        var nameAuthor = decryptionResult.Link.Name.Merge(
            x => decryptionResult.Link.NameAuthorshipClaim.ToAuthorshipResult(x.AuthorshipVerificationFailure),
            _ => new SignatureVerificationError(decryptionResult.Link.NameAuthorshipClaim.Author, "Name decryption failed"));

        var partialNode = new FolderNode
        {
            Uid = uid,
            ParentUid = parentUid,
            Name = nameResult,
            NameAuthor = nameAuthor,
            CreationTime = linkDto.CreationTime,
            TrashTime = linkDto.TrashTime,
            KeyAuthor = nodeAuthor,
            Errors = nodeKeyAndHashKeyErrors,
            OwnedBy = MapOwnedBy(linkDto.OwnedBy),
        };

        var partialSecrets = new FolderOperationData
        {
            ParentUid = parentUid,
            Key = decryptionResult.Link.NodeKey.TryGetValue(out var key) ? key : null,
            PassphraseSessionKey = decryptionResult.Link.Passphrase.Merge(x => (PgpSessionKey?)x.SessionKey, _ => null),
            NameSessionKey = nameSessionKey,
            HashKey = decryptionResult.HashKey.Merge(x => (ReadOnlyMemory<byte>?)x.Data, _ => null),
        };

        return (new FolderMetadata(partialNode, partialSecrets, membershipDto?.ShareId, linkDto.NameHashDigest), failedDecryptionFields);
    }

    private static async ValueTask<PgpPrivateKey> GetEntryPointKeyOrThrowAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkId? parentId,
        ShareAndKey? shareAndKeyToUse,
        ShareId? shareId,
        CancellationToken cancellationToken)
    {
        if (shareId is not null && shareId == shareAndKeyToUse?.Share.Id)
        {
            return shareAndKeyToUse.Value.Key;
        }

        var currentId = parentId;
        var currentShareId = shareId;

        var linkAncestry = new Stack<LinkDetailsDto>(8);

        PgpPrivateKey? lastKey = null;

        // FIXME: this could go into an infinite loop if there's a structure issue in the cache.
        while (currentId is not null)
        {
            if (shareAndKeyToUse is var (shareToUse, shareKeyToUse) && currentId == shareToUse.RootFolderId.LinkId)
            {
                lastKey = shareKeyToUse;
                break;
            }

            var nodeUid = new NodeUid(volumeId, currentId.Value);

            var operationData = await client.Cache.TryGetNodeOperationDataAsync(nodeUid, cancellationToken).ConfigureAwait(false);

            var folderKey = operationData?.Key;

            if (folderKey is not null)
            {
                lastKey = folderKey.Value;
                break;
            }

            var response = await client.Api.Links.GetDetailsAsync(volumeId, [currentId.Value], cancellationToken).ConfigureAwait(false);

            var linkDetails = response.Links is { Count: > 0 } links
                ? links[0]
                : throw new NodeNotFoundException(nodeUid);

            linkAncestry.Push(linkDetails);

            currentShareId = linkDetails.Sharing?.ShareId;

            currentId = linkDetails.Link.ParentId;
        }

        if (lastKey is not { } currentParentKey)
        {
            if (shareAndKeyToUse is not null)
            {
                currentParentKey = shareAndKeyToUse.Value.Key;
            }
            else
            {
                if (currentShareId is null)
                {
                    throw new InvalidOperationException("No share available to access node");
                }

                (_, currentParentKey) = await ShareOperations.GetShareAsync(client, currentShareId.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        while (linkAncestry.TryPop(out var ancestorLinkDetails))
        {
            var decryptionResult = await ConvertDtoToNodeMetadataAsync(
                client,
                volumeId,
                ancestorLinkDetails,
                currentParentKey,
                cancellationToken).ConfigureAwait(false);

            currentParentKey = decryptionResult.GetFolderKeyOrThrow();
        }

        return currentParentKey;
    }

    private static async Task<PgpPrivateKey> GetAlbumEntryPointKeyOrThrowAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        LinkDetailsDto linkDetailsDto,
        ShareAndKey? knownShareAndKey,
        IReadOnlyList<PhotoAlbumInclusionDto> albumInclusions,
        CancellationToken cancellationToken)
    {
        var logger = client.Telemetry.GetLogger("Node metadata");

        // TODO: optimize by selecting the album that is in cache, if any
        // TODO: getting entry point key from the first album should be enough when back-end only returns accessible album IDs
        foreach (var albumInclusionId in albumInclusions.Select(albumInclusion => albumInclusion.Id))
        {
            try
            {
                return await GetEntryPointKeyOrThrowAsync(
                    client,
                    volumeId,
                    albumInclusionId,
                    knownShareAndKey,
                    linkDetailsDto.Sharing?.ShareId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Album \"{Uid}\" not found", new NodeUid(volumeId, albumInclusionId));
            }
        }

        throw new InvalidOperationException("No album entry point key found");
    }

    private static OwnedBy MapOwnedBy(OwnedByDto? dto) => new(dto?.Email, dto?.Organization);
}
