import {
    Logger,
    NodeEntity,
    NodeType,
    NodeWithSameNameExistsValidationError,
    type ProtonDriveClient,
    Thumbnail,
    ValidationError,
} from '@protontech/drive-sdk';
import { generateAdditionalNodeMetadata } from '@protontech/drive-sdk/additionalNodeMetadata';

import { type ActionArgs, type Command, Options, PathType } from '../../cli';
import type { CliMetrics } from '../../telemetry';
import { getSha1 } from './digest';
import { generateThumbnails } from './generateThumbnails';
import { getLocalFileMediaType } from './mediaType';
import { RemoteFolderIndex } from './remoteFolderIndex';
import {
    ConflictChoice,
    ConflictTargetKind,
    getConflictChoicesHelp,
    TransferConflictResolver,
} from './transferConflictResolver';
import { createTransferProgress, TransferProgressInterface, TransferProgressItem } from './transferProgress';
import { type QueueItemDirectory, type QueueItemFile, UploadQueue } from './transferQueue';
import { TransferSummary } from './transferSummary';

const SUPPORTED_REMOTE_PATH_TYPES = [PathType.MyFiles, PathType.Devices, PathType.SharedWithMe];

/**
 * Creates an upload progress callback isolated from the caller's scope.
 *
 * When a closure is defined inside a function, the JS engine attaches it to
 * the entire lexical environment of that function — all variables in scope,
 * whether the closure uses them or not. This means an inline `onProgress`
 * lambda defined inside `uploadBlockData` would keep `encryptedData` (the
 * 4 MB buffer) alive for as long as the HTTP client holds the callback,
 * even though the lambda never references `encryptedData`.
 *
 * By defining this factory at module level, the returned closures only see
 * `fileSize` and `progressTracker`. The file size and progress tracker are
 * invisible to them and can be garbage collected as soon as the upload
 * completes.
 */
export function createUploadProgressCallback(
    fileSize: number,
    progressTracker?: TransferProgressItem,
): ((uploadedBytes: number) => void) | undefined {
    if (!progressTracker) {
        return;
    }

    return (uploadedBytes: number) => {
        // file.size is raw size while uploadedBytes is encrypted size.
        // Encrypted size will be a bit higher. It is enough to cap the
        // progress to 100%.
        if (uploadedBytes <= fileSize) {
            progressTracker.onProgress(uploadedBytes);
        }
    };
}

type UploadContext = {
    logger: Logger;
    sdk: ProtonDriveClient;
    json: boolean;
    skipThumbnails: boolean;
    progress?: TransferProgressInterface;
    uploadQueue: UploadQueue;
    conflictResolver: TransferConflictResolver;
    remoteIndex?: RemoteFolderIndex;
    metrics?: CliMetrics;
};

const FILE_CONFLICT_STRATEGIES = [
    ConflictChoice.CreateNewRevision,
    ConflictChoice.Rename,
    ConflictChoice.TrashRemote,
    ConflictChoice.Skip,
];
const FOLDER_CONFLICT_STRATEGIES = [
    ConflictChoice.Merge,
    ConflictChoice.Rename,
    ConflictChoice.TrashRemote,
    ConflictChoice.Skip,
];

export class CommandFileSystemUpload implements Command {
    group = 'filesystem';
    name = 'upload';
    help =
        'Uploads files and folders. It prompts for conflict resolution unless a strategy option is set. Files with the same content are automatically skipped. Items that already exist are detected by listing the destination folders, making re-uploads of an existing tree cheap.';
    args = ['localPath...', 'parentPath'];
    options: Options = {
        'file-conflict-strategy': {
            type: 'string',
            short: 'f',
            default: '',
            allowedValues: getConflictChoicesHelp(FILE_CONFLICT_STRATEGIES),
            help: 'Conflict strategy applied to files.',
        },
        'folder-conflict-strategy': {
            type: 'string',
            short: 'd',
            default: '',
            allowedValues: getConflictChoicesHelp(FOLDER_CONFLICT_STRATEGIES),
            help: 'Conflict strategy applied to folders.',
        },
        'skip-thumbnails': {
            type: 'boolean',
            short: 't',
            default: false,
            help: 'Skip generating thumbnails.',
        },
        'no-remote-index': {
            type: 'boolean',
            default: false,
            help: 'Do not list destination folders to detect existing items upfront. Slower when re-uploading an existing tree, but avoids listing a folder holding far more items than are uploaded into it.',
        },
    };

    async action({
        logger,
        sdk,
        paths,
        metrics,
        args,
        options: {
            json,
            'file-conflict-strategy': fileConflictStrategy,
            'folder-conflict-strategy': folderConflictStrategy,
            'skip-thumbnails': skipThumbnails,
            'no-remote-index': noRemoteIndex,
        },
    }: ActionArgs) {
        if (args.length < 2) {
            throw new ValidationError('At least one local source and a remote parent path are required');
        }

        const localSources = args.slice(0, -1);
        const parentPathString = args[args.length - 1]!;

        if (!parentPathString.trim()) {
            throw new ValidationError('Remote parent path must not be empty');
        }

        if (localSources.some((p) => !p.trim())) {
            throw new ValidationError('Local source paths must not be empty');
        }

        const parentNode = await paths.getNode(parentPathString, SUPPORTED_REMOTE_PATH_TYPES);

        const summary = new TransferSummary('upload');
        const progress = json ? undefined : createTransferProgress(() => summary.formatProgressLine());

        const conflictResolver = new TransferConflictResolver(logger, {
            fileStrategyChoices: FILE_CONFLICT_STRATEGIES,
            folderStrategyChoices: FOLDER_CONFLICT_STRATEGIES,
            forcedFileStrategy: fileConflictStrategy,
            forcedFolderStrategy: folderConflictStrategy,
            disableInteractiveResolution: json,
            onInteractivePromptBegin: () => progress?.pause(),
            onInteractivePromptEnd: () => progress?.resume(),
        });

        const uploadQueue = new UploadQueue(logger, summary, {
            onDirectory: async (item) => {
                const pending = await this.createFolder(ctx, item);
                if (pending) {
                    await ctx.uploadQueue.enqueueLocalDirectoryChildren(item.localPath, pending.node);
                    return true;
                }
                return false;
            },
            startFile: async (item) => {
                return await this.uploadFile(ctx, item);
            },
        });

        const ctx: UploadContext = {
            logger,
            sdk,
            json,
            skipThumbnails,
            progress,
            uploadQueue,
            conflictResolver,
            remoteIndex: noRemoteIndex ? undefined : new RemoteFolderIndex(sdk, logger),
            metrics,
        };

        try {
            await ctx.uploadQueue.enqueueLocalPaths(localSources, parentNode);
            await ctx.uploadQueue.processQueue();
        } finally {
            progress?.dispose();
            summary.print({ json });
        }

        if (summary.failureCount > 0) {
            throw new ValidationError(`${summary.failureCount} item(s) failed to upload`);
        }
    }

    private async createFolder(
        ctx: UploadContext,
        item: QueueItemDirectory<{ parentNode: NodeEntity }>,
    ): Promise<{ node: NodeEntity } | undefined> {
        let name = item.baseName;

        while (true) {
            // Resolving the conflict from the listing avoids a create that is
            // known to fail plus the metadata fetch of the existing folder.
            const indexedNode = await ctx.remoteIndex?.find(item.parentNode, name);
            if (indexedNode?.type === NodeType.Folder) {
                const resolved = await this.resolveFolderConflict(ctx, item, indexedNode, name);
                if (resolved.done) {
                    return resolved.result;
                }
                name = resolved.name;
                continue;
            }

            try {
                const createdFolder = await ctx.sdk.createFolder(item.parentNode, name);
                ctx.remoteIndex?.add(item.parentNode.uid, createdFolder);
                // A folder we just created has no children, so it never needs
                // to be listed.
                ctx.remoteIndex?.markEmpty(createdFolder.uid);
                return { node: createdFolder };
            } catch (error: unknown) {
                if (!(error instanceof NodeWithSameNameExistsValidationError)) {
                    throw error;
                }
                const existingNodeUid = error.existingNodeUid;
                if (!existingNodeUid) {
                    throw error;
                }

                // The listing did not know about this node, so it is out of
                // date for the whole folder.
                ctx.remoteIndex?.invalidate(item.parentNode.uid);

                const existingNode = await ctx.sdk.getNode(existingNodeUid);

                const resolved = await this.resolveFolderConflict(ctx, item, existingNode, name);
                if (resolved.done) {
                    return resolved.result;
                }
                name = resolved.name;
            }
        }
    }

    /**
     * Applies the folder conflict strategy to an existing remote folder.
     *
     * Returns either the outcome of the upload, or the name to retry the
     * creation with.
     */
    private async resolveFolderConflict(
        ctx: UploadContext,
        item: QueueItemDirectory<{ parentNode: NodeEntity }>,
        existingNode: NodeEntity,
        name: string,
    ): Promise<{ done: true; result: { node: NodeEntity } | undefined } | { done: false; name: string }> {
        const choice = await ctx.conflictResolver.resolve(item.baseName, ConflictTargetKind.Folder);
        switch (choice) {
            case ConflictChoice.Skip:
                return { done: true, result: undefined };
            case ConflictChoice.Merge:
                return { done: true, result: { node: existingNode } };
            case ConflictChoice.TrashRemote:
                await this.trashConflictingNode(ctx, existingNode);
                ctx.remoteIndex?.remove(item.parentNode.uid, name);
                return { done: false, name };
            case ConflictChoice.Rename:
                return { done: false, name: await ctx.sdk.getAvailableName(item.parentNode, item.baseName) };
            default:
                throw new ValidationError(`Unexpected conflict choice: ${choice}`);
        }
    }

    private async uploadFile(
        ctx: UploadContext,
        item: QueueItemFile<{ parentNode: NodeEntity }>,
    ): Promise<number | false> {
        const mediaType = getLocalFileMediaType(ctx.logger, item.localPath);

        let name = item.baseName;
        let newRevisionForNodeUid: string | undefined;

        // Reading the local file is the expensive part, so it is deferred
        // until it is known that the upload can actually happen.
        let content: Awaited<ReturnType<typeof getFileContentMetadata>> | undefined;
        const getContent = async () => (content ??= await getFileContentMetadata(ctx, item, mediaType));

        // Resolving the conflict from the destination listing avoids an upload
        // that is known to be rejected plus the metadata fetch of the existing
        // file. For the skip strategy it avoids reading the local file too.
        const indexedNode = await ctx.remoteIndex?.find(item.parentNode, name);
        if (indexedNode?.type === NodeType.File) {
            if (ctx.conflictResolver.getGlobalStrategy(ConflictTargetKind.File) === ConflictChoice.Skip) {
                return false;
            }

            const { metadata } = await getContent();
            if (indexedNode.activeRevision?.claimedDigests?.sha1 === metadata.expectedSha1) {
                return false;
            }

            const choice = await ctx.conflictResolver.resolve(item.baseName, ConflictTargetKind.File);
            switch (choice) {
                case ConflictChoice.Skip:
                    return false;
                case ConflictChoice.CreateNewRevision:
                    newRevisionForNodeUid = indexedNode.uid;
                    break;
                case ConflictChoice.TrashRemote:
                    await this.trashConflictingNode(ctx, indexedNode);
                    ctx.remoteIndex?.remove(item.parentNode.uid, name);
                    break;
                case ConflictChoice.Rename:
                    name = await ctx.sdk.getAvailableName(item.parentNode, item.baseName);
                    break;
                default:
                    throw new ValidationError(`Unexpected conflict choice: ${choice}`);
            }
        }

        const { file, metadata } = await getContent();
        // Thumbnails are generated only once it is known the content is going
        // to be uploaded, so duplicates never pay for them.
        const thumbnails = await getFileThumbnails(ctx, item.localPath, metadata.mediaType);

        while (true) {
            const progressTracker = ctx.progress?.trackItem(item.baseName, file.size);

            try {
                const uploader = newRevisionForNodeUid
                    ? await ctx.sdk.getFileRevisionUploader(newRevisionForNodeUid, metadata)
                    : await ctx.sdk.getFileUploader(item.parentNode, name, metadata);

                const controller = await uploader.uploadFromStream(
                    file.stream(),
                    thumbnails,
                    createUploadProgressCallback(file.size, progressTracker),
                );

                await controller.completion();
                ctx.metrics?.reportUploadVerifierAttempt();
                return file.size;
            } catch (error: unknown) {
                if (!(error instanceof NodeWithSameNameExistsValidationError)) {
                    throw error;
                }
                const existingNodeUid = error.existingNodeUid;
                if (!existingNodeUid) {
                    throw error;
                }

                // The listing did not know about this node, so it is out of
                // date for the whole folder.
                ctx.remoteIndex?.invalidate(item.parentNode.uid);

                const existingNode = await ctx.sdk.getNode(existingNodeUid);

                // If the existing node is already the same file, automatically skip the upload.
                const existingSha1 = existingNode.activeRevision?.claimedDigests?.sha1;
                if (existingSha1 === metadata.expectedSha1) {
                    return false;
                }

                const choice = await ctx.conflictResolver.resolve(item.baseName, ConflictTargetKind.File);
                switch (choice) {
                    case ConflictChoice.Skip:
                        return false;
                    case ConflictChoice.CreateNewRevision:
                        newRevisionForNodeUid = existingNodeUid;
                        break;
                    case ConflictChoice.TrashRemote:
                        await this.trashConflictingNode(ctx, existingNode);
                        break;
                    case ConflictChoice.Rename:
                        name = await ctx.sdk.getAvailableName(item.parentNode, item.baseName);
                        break;
                    default:
                        throw new ValidationError(`Unexpected conflict choice: ${choice}`);
                }
            } finally {
                progressTracker?.onFinished();
            }
        }
    }

    private async trashConflictingNode(ctx: UploadContext, node: NodeEntity): Promise<void> {
        for await (const result of ctx.sdk.trashNodes([node])) {
            if (!result.ok) {
                throw result.error;
            }
        }
    }
}

/**
 * Reads the local file to produce the metadata needed to upload it.
 *
 * This hashes the whole file, so it is kept separate from thumbnail
 * generation to let callers decide the content is not going to be uploaded
 * before paying for either.
 */
export async function getFileContentMetadata(
    ctx: {
        logger: Logger;
    },
    item: QueueItemFile<{ parentNode: NodeEntity }>,
    mediaType: string,
    additionalMetadataCallback = async (file: Bun.BunFile) =>
        generateAdditionalNodeMetadata(file, mediaType, undefined, ctx.logger),
) {
    const expectedSha1 = await getSha1(item.localPath);
    const file = Bun.file(item.localPath);
    const metadata = {
        mediaType,
        expectedSize: file.size,
        expectedSha1,
        modificationTime: file.lastModified && file.lastModified !== 0 ? new Date(file.lastModified) : undefined,
        ...(await additionalMetadataCallback(file)),
    };

    return {
        file,
        metadata,
    };
}

export async function getFileThumbnails(
    ctx: {
        skipThumbnails: boolean;
    },
    localPath: string,
    mediaType: string | undefined,
): Promise<Thumbnail[]> {
    if (ctx.skipThumbnails) {
        return [];
    }
    try {
        return await generateThumbnails(mediaType || '', localPath);
    } catch (error: unknown) {
        const message = error instanceof Error ? error.message : String(error);
        throw new ValidationError(
            `Failed to generate thumbnails (use --skip-thumbnails to upload without thumbnails): ${message}`,
            undefined,
            { cause: error },
        );
    }
}

export async function getFileMetadata(
    ctx: {
        skipThumbnails: boolean;
        logger: Logger;
    },
    item: QueueItemFile<{ parentNode: NodeEntity }>,
    mediaType: string,
    additionalMetadataCallback = async (file: Bun.BunFile) =>
        generateAdditionalNodeMetadata(file, mediaType, undefined, ctx.logger),
) {
    const { file, metadata } = await getFileContentMetadata(ctx, item, mediaType, additionalMetadataCallback);
    const thumbnails = await getFileThumbnails(ctx, item.localPath, metadata.mediaType);

    return {
        file,
        metadata,
        thumbnails,
    };
}
