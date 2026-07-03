import type { SessionInfo } from 'proton-drive-sdk-account';

export type { SessionInfo };

export type Credentials = {
    cachePassword?: string;
    userKeyPassword: string;
    session: SessionInfo;
    // TODO: Once we have Account SDK, use that over storing with the credentials.
    // This is simplification to avoid too many changes that would be fragile and
    // removed later anyway.
    telemetryEnabled?: boolean;
};

export interface CredentialsStore {
    load(): Promise<Credentials | null>;
    save(snapshot: Credentials): Promise<void>;
    remove(): Promise<void>;
}
