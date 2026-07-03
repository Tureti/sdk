import type { AccountApi } from './accountApi';
import type { Logger } from './logger';
import { fetchTelemetryEnabled } from './telemetryPreference';

function createLogger(): Logger {
    return {
        debug: () => {},
        info: () => {},
        warn: () => {},
        error: () => {},
    };
}

describe('fetchTelemetryEnabled', () => {
    it('returns true when UserSettings.Telemetry is 1', async () => {
        const accountApi = {
            settings: jest.fn().mockResolvedValue({ UserSettings: { Telemetry: 1 } }),
        } as unknown as AccountApi;

        await expect(fetchTelemetryEnabled(accountApi, createLogger())).resolves.toBe(true);
    });

    it('returns false when UserSettings.Telemetry is 0', async () => {
        const accountApi = {
            settings: jest.fn().mockResolvedValue({ UserSettings: { Telemetry: 0 } }),
        } as unknown as AccountApi;

        await expect(fetchTelemetryEnabled(accountApi, createLogger())).resolves.toBe(false);
    });

    it('returns false when the settings request fails', async () => {
        const accountApi = {
            settings: jest.fn().mockRejectedValue(new Error('network error')),
        } as unknown as AccountApi;

        await expect(fetchTelemetryEnabled(accountApi, createLogger())).resolves.toBe(false);
    });
});
