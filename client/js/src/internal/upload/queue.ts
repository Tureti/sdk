import { waitForCondition } from '../wait';
import { FILE_CHUNK_SIZE } from './streamUploader';

/**
 * Maximum number of concurrent file uploads.
 *
 * It avoids uploading too many files at the same time. The total file size
 * below also limits that, but if the file is empty, we still need to make
 * a reasonable number of requests.
 */
const MAX_CONCURRENT_FILE_UPLOADS = 5;

/**
 * Maximum total file size that can be uploaded concurrently.
 *
 * It avoids uploading too many blocks at the same time, ensuring that on poor
 * connection we don't do too many things at the same time that all fail due
 * to network issues.
 */
const MAX_CONCURRENT_UPLOAD_SIZE = 10 * FILE_CHUNK_SIZE;

/**
 * Assumed size-based weight for an upload whose size is not known upfront
 * (`expectedSize: null`).
 *
 * It cannot be `0`: the real size might turn out to be large, and treating
 * it as free would let up to `MAX_CONCURRENT_FILE_UPLOADS` such uploads run
 * fully in parallel - if they are all large and the connection is poor,
 * none of them may finish. It also should not be so large that only one can
 * run at a time, which would be overly conservative for the common case of
 * many small unknown-size uploads. Half of the size budget is a compromise: 
 * at most two unknown-size uploads run concurrently, keeping some parallelism while
 * still biasing toward finishing at least one before starting more.
 */
const UNKNOWN_SIZE_UPLOAD_WEIGHT = MAX_CONCURRENT_UPLOAD_SIZE / 2;

/**
 * A queue that limits the number of concurrent uploads.
 *
 * This is used to limit the number of concurrent uploads to avoid
 * overloading the server, or get rate limited.
 *
 * Each file upload consumes memory and is limited by the number of
 * concurrent block uploads for each file.
 *
 * This queue is straitforward and does not have any priority mechanism
 * or other features, such as limiting total number of blocks being
 * uploaded. That is something we want to add in the future to be
 * more performant for many small file uploads.
 */
export class UploadQueue {
    private totalFileUploads = 0;

    private totalExpectedSize = 0;

    async waitForCapacity(expectedSize: number | null, signal?: AbortSignal) {
        await waitForCondition(
            () =>
                this.totalFileUploads < MAX_CONCURRENT_FILE_UPLOADS &&
                this.totalExpectedSize < MAX_CONCURRENT_UPLOAD_SIZE,
            signal,
        );
        this.totalFileUploads++;
        this.totalExpectedSize += expectedSize ?? UNKNOWN_SIZE_UPLOAD_WEIGHT;
    }

    releaseCapacity(expectedSize: number | null) {
        this.totalFileUploads--;
        this.totalExpectedSize -= expectedSize ?? UNKNOWN_SIZE_UPLOAD_WEIGHT;
    }
}
