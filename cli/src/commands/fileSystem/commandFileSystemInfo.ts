import { NodeEntity, NodeType, ProtonDriveClient } from '@protontech/drive-sdk';
import { type ActionArgs, type Command, printObject, Options, getName, getClaimedSize } from '../../cli';

export class CommandFileSystemInfo implements Command {
    group = 'filesystem';
    name = 'info';
    help = 'Shows full node metadata including latest revision details.';
    args = ['path'];
    options: Options = {
        size: {
            type: 'boolean',
            short: 's',
            default: false,
            help: 'Calculate and include folder size statistics (recursive).',
        },
    };

    async action({ sdk, paths, args: [pathString], options: { json, size } }: ActionArgs) {
        const node = await paths.getNode(pathString);

        if (!size || node.type !== NodeType.Folder) {
            printObject(node, json);
            return;
        }

        const { totalSize, fileCount, folderCount } = await this.calculateFolderStats(sdk, node);

        const result = {
            ...node,
            size: totalSize,
            fileCount,
            folderCount,
        };

        printObject(result, json);
    }

    private async *iterateRecursive(
        sdk: ProtonDriveClient,
        parentNode: NodeEntity | string,
        prefix: string = '',
        depth: number = 0,
    ): AsyncGenerator<NodeEntity> {
        for await (const node of sdk.iterateFolderChildren(parentNode)) {
            const path = prefix ? `${prefix}/${getName(node)}` : getName(node);
            yield Object.assign(node, { treePath: path, treeDepth: depth });
            if (node.type === NodeType.Folder) {
                yield* this.iterateRecursive(sdk, node, path, depth + 1);
            }
        }
    }

    private async calculateFolderStats(
        sdk: ProtonDriveClient,
        folderNode: NodeEntity,
    ): Promise<{ totalSize: number; fileCount: number; folderCount: number }> {
        let totalSize = 0;
        let fileCount = 0;
        let folderCount = 0;

        for await (const node of this.iterateRecursive(sdk, folderNode)) {
            const claimedSize = getClaimedSize(node);
            if (claimedSize !== undefined) {
                totalSize += claimedSize;
            }
            if (node.type === NodeType.File) {
                fileCount++;
            } else if (node.type === NodeType.Folder) {
                folderCount++;
            }
        }

        return { totalSize, fileCount, folderCount };
    }
}
