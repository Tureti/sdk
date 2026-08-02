import { MemberRole, NodeEntity, NodeType, ProtonDriveClient } from '@protontech/drive-sdk';
import { getMockLogger } from '@protontech/drive-sdk/tests/logger';

import { RemoteFolderIndex } from './remoteFolderIndex';

const mockAuthor = { ok: true as const, value: 'a@b.c' };

function mockNode(name: string | undefined, uid: string, type = NodeType.File): NodeEntity {
    return {
        uid,
        name: name !== undefined ? { ok: true, value: name } : { ok: false, error: { name: 'x' } as never },
        type,
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

describe('RemoteFolderIndex', () => {
    const logger = getMockLogger();
    const parent = mockNode('parent', 'p1', NodeType.Folder);

    let iterateFolderChildren: jest.Mock;
    let sdk: ProtonDriveClient;

    function withChildren(...nodes: NodeEntity[]) {
        iterateFolderChildren.mockImplementation(async function* () {
            yield* nodes;
        });
    }

    beforeEach(() => {
        iterateFolderChildren = jest.fn();
        sdk = { iterateFolderChildren } as unknown as ProtonDriveClient;
    });

    it('finds a child by name from the listing', async () => {
        const child = mockNode('file.txt', 'n1');
        withChildren(child, mockNode('other.txt', 'n2'));
        const index = new RemoteFolderIndex(sdk, logger);

        await expect(index.find(parent, 'file.txt')).resolves.toBe(child);
        await expect(index.find(parent, 'missing.txt')).resolves.toBeUndefined();
        expect(iterateFolderChildren).toHaveBeenCalledTimes(1);
        expect(iterateFolderChildren).toHaveBeenCalledWith(parent);
    });

    it('lists a folder only once even for concurrent lookups', async () => {
        withChildren(mockNode('file.txt', 'n1'));
        const index = new RemoteFolderIndex(sdk, logger);

        const results = await Promise.all([
            index.find(parent, 'file.txt'),
            index.find(parent, 'file.txt'),
            index.find(parent, 'other.txt'),
        ]);

        expect(results.map((node) => node?.uid)).toEqual(['n1', 'n1', undefined]);
        expect(iterateFolderChildren).toHaveBeenCalledTimes(1);
    });

    it('does not list a folder marked as empty', async () => {
        const index = new RemoteFolderIndex(sdk, logger);
        index.markEmpty(parent.uid);

        await expect(index.find(parent, 'file.txt')).resolves.toBeUndefined();
        expect(iterateFolderChildren).not.toHaveBeenCalled();
    });

    it('indexes nodes with an undecryptable name under their uid', async () => {
        const child = mockNode(undefined, 'n1');
        withChildren(child);
        const index = new RemoteFolderIndex(sdk, logger);

        await expect(index.find(parent, 'file.txt')).resolves.toBeUndefined();
        await expect(index.find(parent, 'n1')).resolves.toBe(child);
    });

    it('finds nodes added after the listing', async () => {
        withChildren();
        const index = new RemoteFolderIndex(sdk, logger);
        await index.find(parent, 'anything');

        const created = mockNode('new.txt', 'n9');
        index.add(parent.uid, created);

        await expect(index.find(parent, 'new.txt')).resolves.toBe(created);
        expect(iterateFolderChildren).toHaveBeenCalledTimes(1);
    });

    it('does not find nodes that were removed', async () => {
        withChildren(mockNode('file.txt', 'n1'));
        const index = new RemoteFolderIndex(sdk, logger);
        await index.find(parent, 'file.txt');

        index.remove(parent.uid, 'file.txt');

        await expect(index.find(parent, 'file.txt')).resolves.toBeUndefined();
        expect(iterateFolderChildren).toHaveBeenCalledTimes(1);
    });

    it('lists again after invalidation', async () => {
        withChildren(mockNode('file.txt', 'n1'));
        const index = new RemoteFolderIndex(sdk, logger);
        await index.find(parent, 'file.txt');

        index.invalidate(parent.uid);
        const updated = mockNode('file.txt', 'n2');
        withChildren(updated);

        await expect(index.find(parent, 'file.txt')).resolves.toBe(updated);
        expect(iterateFolderChildren).toHaveBeenCalledTimes(2);
    });

    it('degrades to no result when listing fails, without retrying', async () => {
        iterateFolderChildren.mockImplementation(async function* () {
            throw new Error('listing not allowed');
            // eslint-disable-next-line no-unreachable
            yield mockNode('file.txt', 'n1');
        });
        const index = new RemoteFolderIndex(sdk, logger);

        await expect(index.find(parent, 'file.txt')).resolves.toBeUndefined();
        await expect(index.find(parent, 'file.txt')).resolves.toBeUndefined();
        expect(iterateFolderChildren).toHaveBeenCalledTimes(1);
    });

    it('retries a failed listing after invalidation', async () => {
        iterateFolderChildren.mockImplementationOnce(async function* (): AsyncGenerator<NodeEntity> {
            throw new Error('listing not allowed');
        });
        const index = new RemoteFolderIndex(sdk, logger);
        await index.find(parent, 'file.txt');

        index.invalidate(parent.uid);
        const child = mockNode('file.txt', 'n1');
        withChildren(child);

        await expect(index.find(parent, 'file.txt')).resolves.toBe(child);
    });
});
