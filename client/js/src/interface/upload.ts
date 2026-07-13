import { Thumbnail } from './thumbnail';

export type UploadMetadata = {
    mediaType: string;
    /**
     * Expected size of the file.
     *
     * The file size is used to verify the integrity of the file during upload.
     * If the expected size does not match the actual size, the upload will
     * fail.
     *
     * Always pass the real size when it's known - this field drives crucial
     * integrity checks (exact block count and byte total).
     *
     * Pass `null` only when the size genuinely cannot be known upfront (for
     * example, content produced by an on-the-fly export/conversion whose
     * final size is only known once the stream ends). This is an explicit
     * opt-out, not a default: `null` disables those checks - the upload
     * always goes through the streaming path, the queue does not reserve
     * capacity by size, and integrity verification is limited to block
     * completeness and the SHA1 hash (when provided), not the exact byte
     * count.
     */
    expectedSize: number | null;
    /**
     * Expected SHA1 hash of the file content.
     *
     * If provided, the SDK will verify that the SHA1 hash of the uploaded
     * content matches the expected SHA1 hash. If the hashes do not match,
     * the upload will fail with an IntegrityError.
     *
     * The hash should be provided as a hexadecimal string (40 characters).
     */
    expectedSha1?: string;
    /**
     * Modification time of the file.
     *
     * The modification time will be encrypted and stored with the file.
     */
    modificationTime?: Date;
    /**
     * Additional metadata to be stored with the file.
     *
     * These metadata must be object that can be serialized to JSON.
     *
     * The metadata will be encrypted and stored with the file.
     */
    additionalMetadata?: object;
    /**
     * If there is an existing draft by another client, the upload will be
     * rejected. If user decides to override the existing draft and continue
     * with the upload, set this to true.
     */
    overrideExistingDraftByOtherClient?: boolean;
};

export interface FileUploader {
    /**
     * Uploads a file from a stream.
     *
     * The function will resolve to a controller that can be used to pause,
     * resume and complete the upload.
     *
     * The function will reject if the node with the given name already exists.
     */
    uploadFromStream(
        stream: ReadableStream,
        thumnbails: Thumbnail[],
        onProgress?: (uploadedBytes: number) => void,
    ): Promise<UploadController>;

    /**
     * Uploads a file from a file object. It is convenient to use this method
     * when the file is already in memory. The file object is used to get the
     * metadata, such as the media type, size or modification time.
     *
     * The function will resolve to a controller that can be used to pause,
     * resume and complete the upload.
     *
     * The function will reject if the node with the given name already exists.
     */
    uploadFromFile(
        fileObject: File,
        thumnbails: Thumbnail[],
        onProgress?: (uploadedBytes: number) => void,
    ): Promise<UploadController>;
}

export interface UploadController {
    pause(): void;
    resume(): void;
    completion(): Promise<{ nodeRevisionUid: string, nodeUid: string }>;
}
