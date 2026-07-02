import { NodeEntity, ProtonDriveClient, ValidationError } from '@protontech/drive-sdk';
import { splitNodeUid } from '@protontech/drive-sdk/internal/uids';

import { type ActionArgs, type Command, findName, PathType, printIterable, sanitizeTerminalText } from '../../cli';

const SUPPORTED_PATH_TYPES = [PathType.MyFiles, PathType.Devices, PathType.SharedWithMe];

export class CommandFileSystemMove implements Command {
    group = 'filesystem';
    name = 'move';
    help =
        'Moves files and folders. You can move files within the same user. Use `copy` instead to move files between different users.';
    args = ['sourcePath...', 'targetParentPath'];

    async action({ sdk, paths, args, options: { json } }: ActionArgs) {
        const sourcePathStrings = args.slice(0, -1);
        const targetPathString = args[args.length - 1];

        const sourceNodes = await paths.getNodes(sourcePathStrings, SUPPORTED_PATH_TYPES);
        const targetNode = await paths.getNode(targetPathString, SUPPORTED_PATH_TYPES);

        const volumeIds = new Set([...sourceNodes, targetNode].map((node) => splitNodeUid(node.uid).volumeId));
        if (volumeIds.size > 1) {
            throw new ValidationError('Cannot move nodes between different users, use `copy` instead');
        }

        await this.moveNodes(sdk, sourceNodes, targetNode, json);
    }

    private async moveNodes(sdk: ProtonDriveClient, sourceNodes: NodeEntity[], targetNode: NodeEntity, json: boolean) {
        await printIterable(sdk.moveNodes(sourceNodes, targetNode), json, (result) => {
            const nodeName = findName(sourceNodes, result.uid);
            console.log(sanitizeTerminalText(result.ok ? `✅ ${nodeName}` : `❌ ${nodeName}: ${result.error.message}`));
        });
    }
}
