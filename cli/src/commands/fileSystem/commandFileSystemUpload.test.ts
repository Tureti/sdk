import { MemberRole, NodeEntity, NodeType, ThumbnailType, ValidationError } from '@protontech/drive-sdk';
import { getMockLogger } from '@protontech/drive-sdk/tests/logger';

jest.mock('../../cli', () => ({
    PathType: jest.requireActual('../../cli/paths').PathType,
}));
jest.mock('./transferProgress', () => ({
    createTransferProgress: jest.fn(),
}));
jest.mock('./digest');
jest.mock('./generateThumbnails');

import { CommandFileSystemUpload, getFileMetadata } from './commandFileSystemUpload';
import { getSha1 } from './digest';
import { generateThumbnails } from './generateThumbnails';
import type { RemoteFolderIndex } from './remoteFolderIndex';
import { ConflictChoice, TransferConflictResolver } from './transferConflictResolver';
import type { QueueItemDirectory, QueueItemFile } from './transferQueue';

const getSha1Mock = getSha1 as jest.MockedFunction<typeof getSha1>;
const generateThumbnailsMock = generateThumbnails as jest.MockedFunction<typeof generateThumbnails>;

const mockAuthor = { ok: true as const, value: 'a@b.c' };

function mockFolderNode(name: string, uid: string): NodeEntity {
    return {
        uid,
        name: { ok: true, value: name },
        type: NodeType.Folder,
        keyAuthor: mockAuthor,
        nameAuthor: mockAuthor,
        directRole: MemberRole.Admin,
        ownedBy: {},
        isShared: false,
        isSharedByUrl: false,
        creationTime: new Date(),
        modificationTime: new Date(),
        treeEventScopeId: 'scope',
    };
}

describe('getFileMetadata', () => {
    const bunFile = jest.fn();
    const logger = getMockLogger();
    const noAdditionalMetadata: any = async () => ({});

    beforeAll(() => {
        globalThis.Bun = { file: bunFile } as any;
    });

    beforeEach(() => {
        bunFile.mockReset();
        getSha1Mock.mockReset();
        generateThumbnailsMock.mockReset();
    });

    const item: QueueItemFile<{ parentNode: NodeEntity }> = {
        kind: 'file',
        localPath: '/tmp/upload.txt',
        baseName: 'upload.txt',
        parentNode: mockFolderNode('parent', 'p1'),
    };

    function mockBunFile(overrides: { size?: number; lastModified?: number } = {}) {
        const mockFile = {
            size: overrides.size ?? 42,
            lastModified: overrides.lastModified ?? 1_700_000_000_000,
            stream: jest.fn(),
        };
        bunFile.mockReturnValue(mockFile);
        return mockFile;
    }

    it('returns metadata assembled from local file info', async () => {
        const mockFile = mockBunFile();
        getSha1Mock.mockResolvedValue('abc123');
        generateThumbnailsMock.mockResolvedValue([]);

        const result = await getFileMetadata(
            { skipThumbnails: false, logger },
            item,
            'text/plain',
            noAdditionalMetadata,
        );

        expect(getSha1Mock).toHaveBeenCalledWith(item.localPath);
        expect(bunFile).toHaveBeenCalledWith(item.localPath);
        expect(generateThumbnailsMock).toHaveBeenCalledWith('text/plain', item.localPath);
        expect(result.file).toBe(mockFile);
        expect(result.metadata).toEqual({
            mediaType: 'text/plain',
            expectedSize: 42,
            expectedSha1: 'abc123',
            modificationTime: new Date(1_700_000_000_000),
        });
        expect(result.thumbnails).toEqual([]);
    });

    it('omits modificationTime when lastModified is zero', async () => {
        mockBunFile({ size: 10, lastModified: 0 });
        getSha1Mock.mockResolvedValue('deadbeef');
        generateThumbnailsMock.mockResolvedValue([]);

        const result = await getFileMetadata(
            { skipThumbnails: true, logger },
            item,
            'application/octet-stream',
            noAdditionalMetadata,
        );

        expect(result.metadata).toEqual({
            mediaType: 'application/octet-stream',
            expectedSize: 10,
            expectedSha1: 'deadbeef',
            modificationTime: undefined,
        });
    });

    it('skips thumbnail generation when skipThumbnails is true', async () => {
        mockBunFile();
        getSha1Mock.mockResolvedValue('abc123');

        const result = await getFileMetadata(
            { skipThumbnails: true, logger },
            item,
            'image/jpeg',
            noAdditionalMetadata,
        );

        expect(generateThumbnailsMock).not.toHaveBeenCalled();
        expect(result.thumbnails).toEqual([]);
    });

    it('includes generated thumbnails when skipThumbnails is false', async () => {
        mockBunFile();
        getSha1Mock.mockResolvedValue('abc123');
        const thumbnails = [{ type: ThumbnailType.Type1, thumbnail: new Uint8Array([1, 2, 3]) }];
        generateThumbnailsMock.mockResolvedValue(thumbnails);

        const result = await getFileMetadata(
            { skipThumbnails: false, logger },
            item,
            'image/jpeg',
            noAdditionalMetadata,
        );

        expect(result.thumbnails).toBe(thumbnails);
    });

    it('wraps thumbnail generation errors in ValidationError', async () => {
        mockBunFile();
        getSha1Mock.mockResolvedValue('abc123');
        generateThumbnailsMock.mockRejectedValue(new Error('decode failed'));

        await expect(
            getFileMetadata({ skipThumbnails: false, logger }, item, 'image/jpeg', noAdditionalMetadata),
        ).rejects.toThrow(ValidationError);
        await expect(
            getFileMetadata({ skipThumbnails: false, logger }, item, 'image/jpeg', noAdditionalMetadata),
        ).rejects.toThrow(
            'Failed to generate thumbnails (use --skip-thumbnails to upload without thumbnails): decode failed',
        );
    });

    it('wraps non-Error thumbnail generation failures in ValidationError', async () => {
        mockBunFile();
        getSha1Mock.mockResolvedValue('abc123');
        generateThumbnailsMock.mockRejectedValue('decode failed');

        await expect(
            getFileMetadata({ skipThumbnails: false, logger }, item, 'image/jpeg', noAdditionalMetadata),
        ).rejects.toThrow(
            'Failed to generate thumbnails (use --skip-thumbnails to upload without thumbnails): decode failed',
        );
    });
});

function mockFileNode(name: string, uid: string, sha1?: string): NodeEntity {
    return {
        ...mockFolderNode(name, uid),
        type: NodeType.File,
        activeRevision: {
            claimedDigests: sha1 !== undefined ? { sha1, sha1Verified: true } : undefined,
        } as NodeEntity['activeRevision'],
    };
}

describe('upload with the remote folder index', () => {
    const logger = getMockLogger();
    const bunFile = jest.fn();
    const parentNode = mockFolderNode('parent', 'p1');

    const command = new CommandFileSystemUpload();
    // The conflict handling under test lives in private helpers of the
    // command, driven by the transfer queue rather than by `action`.
    const createFolder = (ctx: any, item: QueueItemDirectory<{ parentNode: NodeEntity }>) =>
        (command as any).createFolder(ctx, item);
    const uploadFile = (ctx: any, item: QueueItemFile<{ parentNode: NodeEntity }>) =>
        (command as any).uploadFile(ctx, item);

    let remoteIndex: jest.Mocked<RemoteFolderIndex>;
    let sdk: any;

    function makeCtx(overrides: { fileStrategy?: string; folderStrategy?: string; useIndex?: boolean } = {}) {
        return {
            logger,
            sdk,
            json: true,
            skipThumbnails: false,
            uploadQueue: undefined,
            conflictResolver: new TransferConflictResolver(logger, {
                fileStrategyChoices: [
                    ConflictChoice.CreateNewRevision,
                    ConflictChoice.Rename,
                    ConflictChoice.TrashRemote,
                    ConflictChoice.Skip,
                ],
                folderStrategyChoices: [
                    ConflictChoice.Merge,
                    ConflictChoice.Rename,
                    ConflictChoice.TrashRemote,
                    ConflictChoice.Skip,
                ],
                forcedFileStrategy: overrides.fileStrategy,
                forcedFolderStrategy: overrides.folderStrategy,
                disableInteractiveResolution: true,
            }),
            remoteIndex: overrides.useIndex === false ? undefined : remoteIndex,
        };
    }

    const fileItem: QueueItemFile<{ parentNode: NodeEntity }> = {
        kind: 'file',
        localPath: '/tmp/upload.txt',
        baseName: 'upload.txt',
        parentNode,
    };
    const directoryItem: QueueItemDirectory<{ parentNode: NodeEntity }> = {
        kind: 'directory',
        localPath: '/tmp/photos',
        baseName: 'photos',
        parentNode,
    };

    beforeAll(() => {
        globalThis.Bun = { file: bunFile } as any;
    });

    beforeEach(() => {
        bunFile.mockReset();
        bunFile.mockReturnValue({ size: 42, lastModified: 1_700_000_000_000, type: 'text/plain', stream: jest.fn() });
        getSha1Mock.mockReset();
        getSha1Mock.mockResolvedValue('localsha1');
        generateThumbnailsMock.mockReset();
        generateThumbnailsMock.mockResolvedValue([]);

        remoteIndex = {
            find: jest.fn().mockResolvedValue(undefined),
            markEmpty: jest.fn(),
            add: jest.fn(),
            remove: jest.fn(),
            invalidate: jest.fn(),
        } as unknown as jest.Mocked<RemoteFolderIndex>;

        sdk = {
            createFolder: jest.fn().mockResolvedValue(mockFolderNode('photos', 'created')),
            getNode: jest.fn(),
            getAvailableName: jest.fn(),
            trashNodes: jest.fn(),
            getFileUploader: jest.fn(),
            getFileRevisionUploader: jest.fn(),
        };
    });

    function mockUploader() {
        const controller = { completion: jest.fn().mockResolvedValue({ nodeUid: 'n', nodeRevisionUid: 'r' }) };
        const uploader = { uploadFromStream: jest.fn().mockResolvedValue(controller) };
        sdk.getFileUploader.mockResolvedValue(uploader);
        sdk.getFileRevisionUploader.mockResolvedValue(uploader);
        return uploader;
    }

    describe('files', () => {
        it('skips an indexed file without reading it or calling the API', async () => {
            remoteIndex.find.mockResolvedValue(mockFileNode('upload.txt', 'n1', 'remotesha1'));

            await expect(uploadFile(makeCtx({ fileStrategy: 'skip' }), fileItem)).resolves.toBe(false);

            expect(getSha1Mock).not.toHaveBeenCalled();
            expect(generateThumbnailsMock).not.toHaveBeenCalled();
            expect(sdk.getFileUploader).not.toHaveBeenCalled();
        });

        it('skips an indexed file with identical content before generating thumbnails', async () => {
            remoteIndex.find.mockResolvedValue(mockFileNode('upload.txt', 'n1', 'localsha1'));

            await expect(uploadFile(makeCtx({ fileStrategy: 'create-new-revision' }), fileItem)).resolves.toBe(false);

            expect(getSha1Mock).toHaveBeenCalledWith(fileItem.localPath);
            expect(generateThumbnailsMock).not.toHaveBeenCalled();
            expect(sdk.getFileUploader).not.toHaveBeenCalled();
            expect(sdk.getFileRevisionUploader).not.toHaveBeenCalled();
        });

        it('uploads a new revision of an indexed file with different content', async () => {
            remoteIndex.find.mockResolvedValue(mockFileNode('upload.txt', 'n1', 'remotesha1'));
            mockUploader();

            await expect(uploadFile(makeCtx({ fileStrategy: 'create-new-revision' }), fileItem)).resolves.toBe(42);

            expect(sdk.getFileRevisionUploader).toHaveBeenCalledWith('n1', expect.anything());
            expect(sdk.getFileUploader).not.toHaveBeenCalled();
            expect(generateThumbnailsMock).toHaveBeenCalled();
        });

        it('uploads normally when the index has no entry', async () => {
            mockUploader();

            await expect(uploadFile(makeCtx({ fileStrategy: 'skip' }), fileItem)).resolves.toBe(42);

            expect(sdk.getFileUploader).toHaveBeenCalledWith(parentNode, 'upload.txt', expect.anything());
        });

        it('uploads normally when the indexed entry is a folder', async () => {
            remoteIndex.find.mockResolvedValue(mockFolderNode('upload.txt', 'n1'));
            mockUploader();

            await expect(uploadFile(makeCtx({ fileStrategy: 'skip' }), fileItem)).resolves.toBe(42);

            expect(sdk.getFileUploader).toHaveBeenCalledWith(parentNode, 'upload.txt', expect.anything());
        });

        it('reads the file before prompting when there is no forced strategy', async () => {
            remoteIndex.find.mockResolvedValue(mockFileNode('upload.txt', 'n1', 'remotesha1'));

            // Interactive resolution is disabled, so the conflict surfaces as
            // an error rather than a prompt.
            await expect(uploadFile(makeCtx(), fileItem)).rejects.toThrow(ValidationError);
            expect(getSha1Mock).toHaveBeenCalled();
        });
    });

    describe('folders', () => {
        it('merges into an indexed folder without calling the API', async () => {
            const existing = mockFolderNode('photos', 'n1');
            remoteIndex.find.mockResolvedValue(existing);

            await expect(createFolder(makeCtx({ folderStrategy: 'merge' }), directoryItem)).resolves.toEqual({
                node: existing,
            });
            expect(sdk.createFolder).not.toHaveBeenCalled();
        });

        it('skips an indexed folder without calling the API', async () => {
            remoteIndex.find.mockResolvedValue(mockFolderNode('photos', 'n1'));

            await expect(createFolder(makeCtx({ folderStrategy: 'skip' }), directoryItem)).resolves.toBeUndefined();
            expect(sdk.createFolder).not.toHaveBeenCalled();
        });

        it('records a created folder as known and empty', async () => {
            const created = mockFolderNode('photos', 'created');
            sdk.createFolder.mockResolvedValue(created);

            await expect(createFolder(makeCtx({ folderStrategy: 'merge' }), directoryItem)).resolves.toEqual({
                node: created,
            });
            expect(remoteIndex.add).toHaveBeenCalledWith(parentNode.uid, created);
            expect(remoteIndex.markEmpty).toHaveBeenCalledWith(created.uid);
        });

        it('creates the folder when the indexed entry is a file', async () => {
            remoteIndex.find.mockResolvedValue(mockFileNode('photos', 'n1'));

            await createFolder(makeCtx({ folderStrategy: 'merge' }), directoryItem);

            expect(sdk.createFolder).toHaveBeenCalledWith(parentNode, 'photos');
        });

        it('trashes and recreates an indexed folder for the replace strategy', async () => {
            remoteIndex.find.mockResolvedValueOnce(mockFolderNode('photos', 'n1')).mockResolvedValue(undefined);
            sdk.trashNodes.mockImplementation(async function* () {
                yield { ok: true };
            });

            await createFolder(makeCtx({ folderStrategy: 'replace' }), directoryItem);

            expect(sdk.trashNodes).toHaveBeenCalled();
            expect(remoteIndex.remove).toHaveBeenCalledWith(parentNode.uid, 'photos');
            expect(sdk.createFolder).toHaveBeenCalledWith(parentNode, 'photos');
        });

        it('creates under an available name for the rename strategy', async () => {
            remoteIndex.find.mockResolvedValueOnce(mockFolderNode('photos', 'n1')).mockResolvedValue(undefined);
            sdk.getAvailableName.mockResolvedValue('photos (1)');

            await createFolder(makeCtx({ folderStrategy: 'rename' }), directoryItem);

            expect(sdk.getAvailableName).toHaveBeenCalledWith(parentNode, 'photos');
            expect(sdk.createFolder).toHaveBeenCalledWith(parentNode, 'photos (1)');
        });

        it('creates the folder directly when the index is disabled', async () => {
            await createFolder(makeCtx({ folderStrategy: 'merge', useIndex: false }), directoryItem);

            expect(remoteIndex.find).not.toHaveBeenCalled();
            expect(sdk.createFolder).toHaveBeenCalledWith(parentNode, 'photos');
        });
    });
});
