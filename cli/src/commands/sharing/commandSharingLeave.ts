import { type ActionArgs, type Command } from '../../cli';

export class CommandSharingLeave implements Command {
    group = 'sharing';
    name = 'leave';
    help = 'Leaves a node that was previously shared with you.';
    args = ['path'];

    async action({ paths, args: [pathString], options: { json } }: ActionArgs) {
        const nodePath = paths.getPath(pathString);
        const node = await nodePath.getNode();

        await nodePath.sdk.leaveSharedNode(node);

        if (!json) {
            console.log(`✅ Left shared node`);
        }
    }
}
