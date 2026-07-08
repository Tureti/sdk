package me.proton.drive.sdk.entity

import java.time.Instant

data class FileRevision(
    val uid: RevisionUid,
    val state: RevisionState,
    val creationTime: Instant,
    val storageSize: Long,
    val claimedSize: Long?,
    val claimedDigests: FileContentDigests?,
    val claimedModificationTime: Instant?,
    val thumbnails: List<ThumbnailHeader>,
    val claimedAdditionalMetadata: List<AdditionalMetadataProperty>?,
    val contentAuthor: Result<Author>?,
)
