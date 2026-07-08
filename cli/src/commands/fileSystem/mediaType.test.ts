import path from 'node:path';

import { getMockLogger } from '@protontech/drive-sdk/tests/logger';

import { getLocalFileMediaType } from './mediaType';

const OCTET_STREAM = 'application/octet-stream';

function inferMockMediaType(filePath: string): string {
    const ext = path.extname(filePath);
    const types: Record<string, string> = {
        '.jpg': 'image/jpeg',
        '.jpeg': 'image/jpeg',
        '.png': 'image/png',
        '.txt': 'text/plain;charset=utf-8',
    };
    return types[ext] ?? OCTET_STREAM;
}

describe('mediaType', () => {
    const bunFile = jest.fn((filePath: string) => ({
        get type() {
            if (filePath.includes('throw-on-fallback') && filePath.endsWith('.jpg')) {
                throw new Error('type lookup failed');
            }
            return inferMockMediaType(filePath);
        },
    }));

    beforeAll(() => {
        globalThis.Bun = { file: bunFile } as any;
    });

    beforeEach(() => {
        bunFile.mockClear();
    });

    describe('getLocalFileMediaType', () => {
        it('returns the type for a lowercase extension', () => {
            const filePath = path.join('folder', 'photo.jpg');

            expect(getLocalFileMediaType(getMockLogger(), filePath)).toBe('image/jpeg');
            expect(bunFile).toHaveBeenCalledTimes(1);
            expect(bunFile).toHaveBeenCalledWith(filePath);
        });

        it('falls back to a lowercase extension for uppercase extensions', () => {
            const filePath = path.join('folder', 'photo.JPG');

            expect(getLocalFileMediaType(getMockLogger(), filePath)).toBe('image/jpeg');
            expect(bunFile).toHaveBeenCalledTimes(2);
            expect(bunFile).toHaveBeenNthCalledWith(1, filePath);
            expect(bunFile).toHaveBeenNthCalledWith(2, path.join('folder', 'photo.jpg'));
        });

        it('returns application/octet-stream for an unknown extension', () => {
            const filePath = path.join('folder', 'archive.xyz');

            expect(getLocalFileMediaType(getMockLogger(), filePath)).toBe(OCTET_STREAM);
            expect(bunFile).toHaveBeenCalledTimes(1);
        });

        it('returns application/octet-stream when the path has no extension', () => {
            const filePath = path.join('folder', 'readme');

            expect(getLocalFileMediaType(getMockLogger(), filePath)).toBe(OCTET_STREAM);
            expect(bunFile).toHaveBeenCalledTimes(1);
        });

        it('logs and returns application/octet-stream when the fallback lookup fails', () => {
            const logger = getMockLogger();
            const filePath = path.join('throw-on-fallback', 'photo.JPG');

            expect(getLocalFileMediaType(logger, filePath)).toBe(OCTET_STREAM);
            expect(logger.error).toHaveBeenCalledWith('Failed to get local file media type', expect.any(Error));
        });
    });
});
