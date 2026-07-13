import { existsSync, writeSync } from 'node:fs';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { LogLevel } from '@protontech/drive-sdk/telemetry';

import { FileLogHandler } from './fileHandler';

const LOG_FILE_NAME = 'proton-drive.log';

describe('FileLogHandler', () => {
    let logDir: string;

    beforeAll(() => {
        globalThis.Bun = {
            file: (source: string | number) => ({
                writer() {
                    if (typeof source !== 'number') {
                        throw new Error('Expected file descriptor');
                    }

                    const fd = source;
                    return {
                        write(data: string) {
                            writeSync(fd, data);
                        },
                        flush() {},
                        end() {},
                    };
                },
            }),
        } as typeof Bun;
    });

    beforeEach(async () => {
        logDir = await mkdtemp(path.join(os.tmpdir(), 'drive-cli-file-handler-'));
    });

    afterEach(async () => {
        await rm(logDir, { recursive: true, force: true });
    });

    it('appends to an existing log file', async () => {
        const logFile = path.join(logDir, LOG_FILE_NAME);
        await writeFile(logFile, 'existing line\n', 'utf8');

        const handler = new FileLogHandler(logDir, { maxLogBytes: 100 });
        handler.log(makeLog('new line'));

        const content = await readFile(logFile, 'utf8');
        expect(content).toContain('existing line');
        expect(content).toContain('new line');
    });

    it('appends to an existing log file long line', async () => {
        const logFile = path.join(logDir, LOG_FILE_NAME);
        await writeFile(logFile, '', 'utf8');

        const handler = new FileLogHandler(logDir, { maxLogBytes: 1 });
        handler.log(makeLog('very long line'));

        const content = await readFile(logFile, 'utf8');
        expect(content).toContain('very long line');
        expect(existsSync(`${logFile}.1`)).toBe(false);
    });

    it('rotates on startup when the log file exceeds the size limit', async () => {
        const logFile = path.join(logDir, LOG_FILE_NAME);
        await writeFile(logFile, 'x'.repeat(100), 'utf8');

        const handler = new FileLogHandler(logDir, { maxLogBytes: 100 });
        handler.log(makeLog('after rotation'));

        const rotatedContent = await readFile(`${logFile}.1`, 'utf8');
        const currentContent = await readFile(logFile, 'utf8');

        expect(rotatedContent).toBe('x'.repeat(100));
        expect(currentContent).toContain('after rotation');
    });

    it('rotates while writing when the next line would exceed the size limit', async () => {
        const logFile = path.join(logDir, LOG_FILE_NAME);
        const handler = new FileLogHandler(logDir, { maxLogBytes: 200 });

        handler.log(makeLog('a'.repeat(150)));
        handler.log(makeLog('b'.repeat(10)));
        await handler.flush();

        const rotatedContent = await readFile(`${logFile}.1`, 'utf8');
        const currentContent = await readFile(logFile, 'utf8');

        expect(rotatedContent).toContain('a'.repeat(150));
        expect(currentContent).toContain('b'.repeat(10));
    });

    it('drops the oldest rotated log when the rotation limit is reached', async () => {
        const logFile = path.join(logDir, LOG_FILE_NAME);
        await writeFile(`${logFile}.1`, 'first\n', 'utf8');
        await writeFile(`${logFile}.2`, 'second\n', 'utf8');
        await writeFile(`${logFile}.3`, 'third\n', 'utf8');
        await writeFile(logFile, 'x'.repeat(100), 'utf8');

        const handler = new FileLogHandler(logDir, { maxLogBytes: 100, maxRotatedLogs: 3 });
        handler.log(makeLog('current'));

        expect(await readFile(`${logFile}.3`, 'utf8')).toBe('second\n');
        expect(await readFile(`${logFile}.2`, 'utf8')).toBe('first\n');
        expect(await readFile(`${logFile}.1`, 'utf8')).toBe('x'.repeat(100));
        expect(await readFile(logFile, 'utf8')).toContain('current');
    });
});

function makeLog(message: string) {
    return {
        time: new Date('2024-01-01T00:00:00.000Z'),
        level: LogLevel.INFO,
        loggerName: 'test',
        message,
    };
}
