import { Logger, ValidationError } from '@protontech/drive-sdk';

import { PASS_CREDENTIALS_PATH } from './constants';
import type { Credentials, CredentialsStore } from './interface';
import { parseStoredSnapshot } from './parseCredentials';

type PassResult = {
    stdout: string;
    stderr: string;
    exitCode: number;
};

export type PassRunner = (args: string[], stdin?: string) => Promise<PassResult>;

export class PassSessionStore implements CredentialsStore {
    constructor(
        private readonly logger: Logger,
        private readonly runPassFn: PassRunner = runPass,
    ) {}

    async load(): Promise<Credentials | null> {
        this.logger.debug(`Loading session from pass: ${PASS_CREDENTIALS_PATH}`);
        let result: PassResult;
        try {
            result = await this.runPassFn(['show', PASS_CREDENTIALS_PATH]);
        } catch (error: unknown) {
            const message = error instanceof Error ? error.message : String(error);
            throw new ValidationError(
                `Failed to load session from pass (ensure pass is installed and gpg-agent can decrypt): ${message}`,
                undefined,
                { cause: error },
            );
        }
        if (result.exitCode !== 0) {
            if (isPassEntryMissing(result)) {
                return null;
            }
            throw new ValidationError(passFailureMessage('load', result));
        }
        return parseStoredSnapshot(result.stdout);
    }

    async save(snapshot: Credentials): Promise<void> {
        this.logger.debug(`Saving session to pass: ${PASS_CREDENTIALS_PATH}`);
        const result = await this.runPassFn(['insert', '-f', '-m', PASS_CREDENTIALS_PATH], JSON.stringify(snapshot));
        if (result.exitCode !== 0) {
            throw new ValidationError(passFailureMessage('save', result));
        }
    }

    async remove(): Promise<void> {
        this.logger.debug(`Removing session from pass: ${PASS_CREDENTIALS_PATH}`);
        const result = await this.runPassFn(['rm', '-f', PASS_CREDENTIALS_PATH]);
        if (result.exitCode !== 0 && !isPassEntryMissing(result)) {
            throw new ValidationError(passFailureMessage('remove', result));
        }
    }
}

async function runPass(args: string[], stdin?: string): Promise<PassResult> {
    const proc = Bun.spawn(['pass', ...args], {
        stdin: stdin !== undefined ? 'pipe' : 'ignore',
        stdout: 'pipe',
        stderr: 'pipe',
    });
    if (stdin !== undefined && proc.stdin) {
        await proc.stdin.write(stdin);
        await proc.stdin.end();
    }
    const [stdout, stderr, exitCode] = await Promise.all([
        new Response(proc.stdout).text(),
        new Response(proc.stderr).text(),
        proc.exited,
    ]);
    return { stdout, stderr, exitCode };
}

function passFailureMessage(action: string, result: PassResult): string {
    return `Failed to ${action} session in pass: ${result.stderr.trim() || `exit code ${result.exitCode}`}`;
}

function isPassEntryMissing(result: PassResult): boolean {
    return result.stderr.includes('is not in the password store');
}
