import { type ActionArgs, type Command } from '../../cli';

export class CommandSharingLeave implements Command {
    group = 'sharing';
    name = 'leave';
    help = 'Leaves a node that was previously shared with you. Get the UID from the shared-with-me listing.';
    args = ['nodeUid'];

    async action({ sdk, args: [nodeUid], options: { json } }: ActionArgs) {
        await sdk.leaveSharedNode(nodeUid);

        if (!json) {
            console.log(`✅ Left shared node`);
        }
    }
}
