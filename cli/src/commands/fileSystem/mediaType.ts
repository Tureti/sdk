import path from 'node:path';

import { Logger } from '@protontech/drive-sdk';

const OCTET_STREAM = 'application/octet-stream';

/**
 * Returns the media type of the local file.
 */
export function getLocalFileMediaType(logger: Logger, localPath: string): string {
    const type = Bun.file(localPath).type;
    if (type && type !== OCTET_STREAM) {
        return type;
    }

    // Bun's type detection is based on the file extension. Uppercase
    // extensions are not supported (.JPG returns application/octet-stream).
    // Bun.file can be used also for non-existing files when used only for
    // the type property. Just in case, the call here is wrapped in case
    // this behavior changes in the future.
    try {
        const ext = path.extname(localPath);
        if (ext.length <= 1 || ext === ext.toLowerCase()) {
            return type || OCTET_STREAM;
        }

        const lowerExtType = Bun.file(`${localPath.slice(0, -ext.length)}${ext.toLowerCase()}`).type;
        if (lowerExtType && lowerExtType !== OCTET_STREAM) {
            return lowerExtType;
        }
    } catch (error) {
        logger.error('Failed to get local file media type', error);
    }

    return type || OCTET_STREAM;
}
