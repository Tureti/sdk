jest.mock('ky', () => {
    const create = () => ({
        extend: jest.fn().mockReturnThis(),
        post: jest.fn(),
        get: jest.fn(),
    });

    return {
        __esModule: true,
        default: {
            create,
        },
    };
});

import { ApiClient } from './apiClient';
import type { Logger } from './logger';
import type { SessionCredentials } from './sessionCredentials';

function createLogger(): Logger {
    return {
        debug: () => {},
        info: () => {},
        warn: () => {},
        error: () => {},
    };
}

function createSessionCredentials(overrides?: Partial<SessionCredentials>): SessionCredentials {
    let uid = 'uid-1';
    let accessToken = 'access-1';
    let refreshToken = 'refresh-1';
    const listeners = new Set<() => void>();

    return {
        get uid() {
            return uid;
        },
        get accessToken() {
            return accessToken;
        },
        get refreshToken() {
            return refreshToken;
        },
        on(event, callback) {
            if (event === 'sessionInfoChanged') {
                listeners.add(callback);
            }
        },
        isLoggedIn: () => true,
        isTelemetryEnabled: () => false,
        getUserKeyPassword: () => undefined,
        load: async () => {},
        setUserKeyPassword: async () => {},
        setSessionInfo: async (info) => {
            uid = info.uid;
            accessToken = info.accessToken;
            refreshToken = info.refreshToken ?? refreshToken;
            listeners.forEach((callback) => callback());
        },
        setTelemetryEnabled: async () => {},
        signOut: async () => {},
        ...overrides,
    };
}

describe('ApiClient refresh coordination', () => {
    let credentials: SessionCredentials;
    let apiClient: ApiClient;
    let performTokenRefreshSpy: jest.SpyInstance;

    beforeEach(() => {
        credentials = createSessionCredentials();
        apiClient = new ApiClient({
            baseUrl: 'https://example.com',
            appVersion: 'test',
            credentials,
            logger: createLogger(),
        });
        performTokenRefreshSpy = jest.spyOn(
            apiClient as unknown as { performTokenRefresh: () => Promise<boolean> },
            'performTokenRefresh',
        )
    });

    it('deduplicates concurrent refresh attempts', async () => {
        let resolveRefresh: (() => void) | undefined;
        const refreshGate = new Promise<void>((resolve) => {
            resolveRefresh = resolve;
        });

        performTokenRefreshSpy.mockImplementation(async () => {
            await refreshGate;
            return true;
        });

        const firstRefresh = apiClient.refreshSessionIfPossible('access-1');
        const secondRefresh = apiClient.refreshSessionIfPossible('access-1');

        expect(performTokenRefreshSpy).toHaveBeenCalledTimes(1);

        resolveRefresh?.();
        const [firstResult, secondResult] = await Promise.all([firstRefresh, secondRefresh]);

        expect(firstResult).toBe(true);
        expect(secondResult).toBe(true);
        expect(performTokenRefreshSpy).toHaveBeenCalledTimes(1);
    });

    it('returns true without refreshing when another request already refreshed the token', async () => {
        await credentials.setSessionInfo({
            uid: 'uid-1',
            accessToken: 'access-2',
            refreshToken: 'refresh-2',
        });

        await expect(apiClient.refreshSessionIfPossible('access-1')).resolves.toBe(true);
        expect(performTokenRefreshSpy).not.toHaveBeenCalled();
    });
});
