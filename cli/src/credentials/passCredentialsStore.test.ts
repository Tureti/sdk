import { type Logger, ValidationError } from '@protontech/drive-sdk';

import { PASS_CREDENTIALS_PATH } from './constants';
import type { Credentials } from './interface';
import { type PassRunner, PassSessionStore } from './passCredentialsStore';

const logger = { debug: () => {} } as unknown as Logger;

const snapshot: Credentials = {
    userKeyPassword: 'user-key-pass',
    session: {
        uid: 'uid-1',
        accessToken: 'access-token',
        refreshToken: 'refresh-token',
    },
    cachePassword: 'cache-pass',
};

const storedSnapshot = JSON.stringify(snapshot);

function createStore(runPassFn: PassRunner) {
    return new PassSessionStore(logger, runPassFn);
}

describe('PassSessionStore', () => {
    describe('load', () => {
        it('returns parsed credentials when pass show succeeds', async () => {
            const store = createStore(async () => ({
                stdout: storedSnapshot,
                stderr: '',
                exitCode: 0,
            }));

            await expect(store.load()).resolves.toEqual(snapshot);
        });

        it('returns null when the pass entry is missing', async () => {
            const store = createStore(async () => ({
                stdout: '',
                stderr: `Password store entry for ${PASS_CREDENTIALS_PATH} is not in the password store.`,
                exitCode: 1,
            }));

            await expect(store.load()).resolves.toBeNull();
        });

        it('throws ValidationError when pass show fails for another reason', async () => {
            const store = createStore(async () => ({
                stdout: '',
                stderr: 'gpg: decryption failed: No secret key',
                exitCode: 2,
            }));

            await expect(store.load()).rejects.toThrow(
                new ValidationError('Failed to load session in pass: gpg: decryption failed: No secret key'),
            );
        });

        it('wraps spawn failures in ValidationError', async () => {
            const store = createStore(async () => {
                throw new Error('pass: command not found');
            });

            await expect(store.load()).rejects.toThrow(
                new ValidationError(
                    'Failed to load session from pass (ensure pass is installed and gpg-agent can decrypt): pass: command not found',
                ),
            );
        });
    });

    describe('save', () => {
        it('inserts the serialized snapshot into pass', async () => {
            const calls: Array<{ args: string[]; stdin?: string }> = [];
            const store = createStore(async (args, stdin) => {
                calls.push({ args, stdin });
                return { stdout: '', stderr: '', exitCode: 0 };
            });

            await store.save(snapshot);

            expect(calls).toEqual([
                {
                    args: ['insert', '-f', '-m', PASS_CREDENTIALS_PATH],
                    stdin: storedSnapshot,
                },
            ]);
        });

        it('throws ValidationError when pass insert fails', async () => {
            const store = createStore(async () => ({
                stdout: '',
                stderr: 'gpg: signing failed',
                exitCode: 1,
            }));

            await expect(store.save(snapshot)).rejects.toThrow(
                new ValidationError('Failed to save session in pass: gpg: signing failed'),
            );
        });
    });

    describe('remove', () => {
        it('removes the pass entry', async () => {
            const calls: string[][] = [];
            const store = createStore(async (args) => {
                calls.push(args);
                return { stdout: '', stderr: '', exitCode: 0 };
            });

            await store.remove();

            expect(calls).toEqual([['rm', '-f', PASS_CREDENTIALS_PATH]]);
        });

        it('ignores a missing pass entry', async () => {
            const store = createStore(async () => ({
                stdout: '',
                stderr: `Password store entry for ${PASS_CREDENTIALS_PATH} is not in the password store.`,
                exitCode: 1,
            }));

            await expect(store.remove()).resolves.toBeUndefined();
        });

        it('throws ValidationError when pass rm fails for another reason', async () => {
            const store = createStore(async () => ({
                stdout: '',
                stderr: 'permission denied',
                exitCode: 1,
            }));

            await expect(store.remove()).rejects.toThrow(
                new ValidationError('Failed to remove session in pass: permission denied'),
            );
        });
    });
});
