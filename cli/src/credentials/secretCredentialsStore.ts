import { Logger, ValidationError } from '@protontech/drive-sdk';

import { CREDENTIALS_NAME, CREDENTIALS_SERVICE } from './constants';
import type { Credentials, CredentialsStore } from './interface';
import { parseStoredSnapshot } from './parseCredentials';

export class SecretsSessionStore implements CredentialsStore {
    constructor(private readonly logger: Logger) {}

    async load(): Promise<Credentials | null> {
        this.logger.debug(`Loading session ${CREDENTIALS_NAME} from secrets`);
        let raw;
        try {
            raw = await Bun.secrets.get({ service: CREDENTIALS_SERVICE, name: CREDENTIALS_NAME });
        } catch (error: unknown) {
            const message = error instanceof Error ? error.message : String(error);
            throw new ValidationError(
                `Failed to load session from secrets (ensure you have secrets available, read the README for more information): ${message}`,
                undefined,
                { cause: error },
            );
        }
        return parseStoredSnapshot(raw);
    }

    async save(snapshot: Credentials): Promise<void> {
        this.logger.debug(`Saving session ${CREDENTIALS_NAME} to secrets`);
        await Bun.secrets.set({
            service: CREDENTIALS_SERVICE,
            name: CREDENTIALS_NAME,
            value: JSON.stringify(snapshot),
        });
    }

    async remove(): Promise<void> {
        this.logger.debug(`Removing session ${CREDENTIALS_NAME} from secrets`);
        await Bun.secrets.delete({ service: CREDENTIALS_SERVICE, name: CREDENTIALS_NAME });
    }
}
