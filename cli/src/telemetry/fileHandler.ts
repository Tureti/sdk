import { closeSync, existsSync, openSync, renameSync, statSync, unlinkSync } from 'node:fs';
import path from 'node:path';

import {
    BasicLogFormatter,
    LogFormatter,
    LogHandler,
    LogRecord,
    MetricEvent,
    MetricRecord,
} from '@protontech/drive-sdk/telemetry';

const LOG_FILE_NAME = 'proton-drive.log';
const DEFAULT_MAX_LOG_BYTES = 5 * 1024 * 1024;
const DEFAULT_MAX_ROTATED_LOGS = 5;

export type FileLogHandlerOptions = {
    maxLogBytes?: number;
    maxRotatedLogs?: number;
};

export class FileLogHandler implements LogHandler {
    private formatter: LogFormatter;

    private logFilePath: string;
    private logFileSize: number;
    private maxLogBytes: number;
    private maxRotatedLogs: number;

    private fd!: number;
    private writer!: Bun.FileSink;
    private writeQueue = Promise.resolve();

    constructor(logDir: string, options: FileLogHandlerOptions = {}) {
        this.formatter = new BasicLogFormatter();
        this.logFilePath = path.join(logDir, LOG_FILE_NAME);
        this.maxLogBytes = options.maxLogBytes ?? DEFAULT_MAX_LOG_BYTES;
        this.maxRotatedLogs = options.maxRotatedLogs ?? DEFAULT_MAX_ROTATED_LOGS;

        this.logFileSize = getLogFileSize(this.logFilePath);
        if (this.logFileSize >= this.maxLogBytes) {
            rotateLogFile(this.logFilePath, this.maxRotatedLogs);
            this.logFileSize = 0;
        }

        this.openWriter();
    }

    log(log: LogRecord) {
        this.enqueueWrite(this.formatter.format(log));
    }

    onEvent(metric: MetricRecord<MetricEvent>) {
        this.enqueueWrite(JSON.stringify(metric));
    }

    flush(): Promise<void> {
        return this.writeQueue;
    }

    private enqueueWrite(message: string) {
        this.writeQueue = this.writeQueue.then(() => this.writeLine(message)).catch(() => {});
    }

    private async writeLine(message: string) {
        const line = `${message}\n`;
        const lineLength = Buffer.byteLength(line, 'utf8');

        if (this.logFileSize + lineLength >= this.maxLogBytes && this.logFileSize > 0) {
            await this.closeWriter();
            rotateLogFile(this.logFilePath, this.maxRotatedLogs);
            this.logFileSize = 0;
            this.openWriter();
        }

        await this.writer.write(message);
        await this.writer.write('\n');
        await this.writer.flush();
        this.logFileSize += lineLength;
    }

    private openWriter() {
        this.fd = openSync(this.logFilePath, 'a');
        this.writer = Bun.file(this.fd).writer();
    }

    private async closeWriter() {
        await this.writer.end();
        closeSync(this.fd);
    }
}

function getLogFileSize(logFile: string): number {
    try {
        return statSync(logFile).size;
    } catch {
        return 0;
    }
}

function rotateLogFile(logFilePath: string, maxRotatedLogs: number): void {
    const rotatedPath = (index: number) => `${logFilePath}.${index}`;

    const oldest = rotatedPath(maxRotatedLogs);
    if (existsSync(oldest)) {
        unlinkSync(oldest);
    }

    for (let i = maxRotatedLogs - 1; i >= 1; i--) {
        const from = rotatedPath(i);
        const to = rotatedPath(i + 1);
        if (existsSync(from)) {
            renameSync(from, to);
        }
    }

    if (existsSync(logFilePath)) {
        renameSync(logFilePath, rotatedPath(1));
    }
}
