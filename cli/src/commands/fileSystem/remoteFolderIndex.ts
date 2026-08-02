import type { Logger, NodeEntity, ProtonDriveClient } from '@protontech/drive-sdk';

import { getName } from '../../cli/node';

type FolderChildren = Map<string, NodeEntity>;

/**
 * Lazy, in-memory index of the children of remote folders, keyed by name.
 *
 * The upload walk otherwise discovers existing remote items by attempting the
 * write and catching the `ALREADY_EXISTS` error from the API. That costs a
 * doomed create plus a single-item metadata fetch for every item that is
 * already there, which dominates the runtime of a backup where almost
 * everything is a duplicate. Listing a folder once instead costs a single
 * paginated children request plus batched metadata fetches, and lets the
 * duplicates be skipped without touching the network at all.
 *
 * The index is only an optimisation: the callers keep their `ALREADY_EXISTS`
 * handling, so a stale, incomplete or failed index can never produce a wrong
 * result, only a slower one.
 */
export class RemoteFolderIndex {
    private children = new Map<string, FolderChildren>();
    private loading = new Map<string, Promise<FolderChildren | undefined>>();
    private unindexable = new Set<string>();

    constructor(
        private readonly sdk: ProtonDriveClient,
        private readonly logger: Logger,
    ) {}

    /**
     * Returns the child of `parentNode` with the given name, listing the
     * folder on first use.
     *
     * Returns `undefined` when there is no such child, or when the folder
     * could not be listed. Names are read with `getName`, which falls back to
     * the node UID for nodes whose name could not be decrypted; such nodes
     * therefore never match a local name and fall back to the API path.
     */
    async find(parentNode: NodeEntity, name: string): Promise<NodeEntity | undefined> {
        const children = await this.getChildren(parentNode);
        return children?.get(name);
    }

    /**
     * Records that `nodeUid` is known to have no children.
     *
     * Called for folders created during the upload, so that uploading a fresh
     * tree never pays for a listing.
     */
    markEmpty(nodeUid: string): void {
        this.unindexable.delete(nodeUid);
        this.children.set(nodeUid, new Map());
    }

    /** Records a node that was just created under `parentNodeUid`. */
    add(parentNodeUid: string, node: NodeEntity): void {
        this.children.get(parentNodeUid)?.set(getName(node), node);
    }

    /** Forgets a node that was just removed from `parentNodeUid`. */
    remove(parentNodeUid: string, name: string): void {
        this.children.get(parentNodeUid)?.delete(name);
    }

    /** Drops what is known about `nodeUid`'s children, forcing a fresh listing. */
    invalidate(nodeUid: string): void {
        this.children.delete(nodeUid);
        this.loading.delete(nodeUid);
        this.unindexable.delete(nodeUid);
    }

    private async getChildren(parentNode: NodeEntity): Promise<FolderChildren | undefined> {
        const known = this.children.get(parentNode.uid);
        if (known) {
            return known;
        }
        if (this.unindexable.has(parentNode.uid)) {
            return;
        }

        // Files are uploaded concurrently, so several items sharing a parent
        // can reach this point before the first listing finished. Share the
        // in-flight promise so the folder is listed only once.
        let loading = this.loading.get(parentNode.uid);
        if (!loading) {
            loading = this.loadChildren(parentNode);
            this.loading.set(parentNode.uid, loading);
        }
        try {
            return await loading;
        } finally {
            this.loading.delete(parentNode.uid);
        }
    }

    private async loadChildren(parentNode: NodeEntity): Promise<FolderChildren | undefined> {
        const children: FolderChildren = new Map();
        try {
            for await (const child of this.sdk.iterateFolderChildren(parentNode)) {
                children.set(getName(child), child);
            }
        } catch (error: unknown) {
            // Listing is best effort. Falling back to the API path keeps the
            // upload working, just without the fast duplicate detection.
            this.logger.debug(`Failed to list children of ${parentNode.uid}, using conflict errors instead: ${error}`);
            this.unindexable.add(parentNode.uid);
            return;
        }
        this.children.set(parentNode.uid, children);
        return children;
    }
}
