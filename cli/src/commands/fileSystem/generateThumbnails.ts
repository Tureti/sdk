import { randomUUID } from 'node:crypto';
import { unlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { type Thumbnail, ThumbnailType } from '@protontech/drive-sdk';

const MAX_THUMBNAIL_SIDE = 512;
const MAX_THUMBNAIL_BYTES = 64 * 1024 - 512;

const MAX_HD_THUMBNAIL_SIDE = 1920;
const MAX_HD_THUMBNAIL_BYTES = 1024 * 1024 - 512;

const IMAGE_MEDIA_TYPES = new Set([
    'image/jpeg',
    'image/jpg',
    'image/png',
    'image/gif',
    'image/bmp',
    'image/tiff',
    'image/webp',
]);

// Containers Bun sometimes fails to map to a `video/*` media type (e.g. `.mkv`
// is reported as `application/octet-stream`). ffmpeg can decode all of them.
const VIDEO_EXTENSIONS = /\.(mp4|m4v|mov|webm|mkv|avi|mpe?g|3gp|flv|wmv|ogv)$/i;

export async function generateThumbnails(mediaType: string, localPath: string): Promise<Thumbnail[]> {
    if (isVideoMediaType(mediaType, localPath)) {
        return generateVideoThumbnails(localPath);
    }
    if (!IMAGE_MEDIA_TYPES.has(mediaType.trim().toLowerCase())) {
        return [];
    }

    // Reuse already decoded image for thumbnail generation (official change)
    const image = new Bun.Image(localPath, { autoOrient: true });
    const metadata = await image.metadata();
    const width = metadata.width;
    const height = metadata.height;

    if (width === 0 || height === 0) {
        return [];
    }

    const type1Buffer = await webpFitMaxBytes(image, MAX_THUMBNAIL_SIDE, MAX_THUMBNAIL_BYTES);
    if (!type1Buffer) {
        return [];
    }

    const thumbnails: Thumbnail[] = [
        {
            type: ThumbnailType.Type1,
            thumbnail: new Uint8Array(type1Buffer),
        },
    ];

    if (shouldGenerateHdThumbnail(width, height, mediaType)) {
        const type2Buffer = await webpFitMaxBytes(image, MAX_HD_THUMBNAIL_SIDE, MAX_HD_THUMBNAIL_BYTES);
        if (type2Buffer) {
            thumbnails.push({
                type: ThumbnailType.Type2,
                thumbnail: new Uint8Array(type2Buffer),
            });
        }
    }

    return thumbnails;
}

function isVideoMediaType(mediaType: string, localPath: string): boolean {
    if (mediaType.trim().toLowerCase().startsWith('video/')) {
        return true;
    }
    return VIDEO_EXTENSIONS.test(localPath);
}

async function generateVideoThumbnails(localPath: string): Promise<Thumbnail[]> {
    const ffmpegPath = findFfmpeg();
    if (!ffmpegPath) {
        warnFfmpegMissingOnce();
        return [];
    }

    const tempPath = join(tmpdir(), `pd-thumb-${randomUUID()}.png`);
    try {
        const ffprobePath = findFfprobe();
        const duration = ffprobePath ? await probeDuration(ffprobePath, localPath) : 0;
        // Seek 10% into the video before grabbing a frame — the same default as
        // ffmpegthumbnailer, which avoids leading black/title frames.
        const seek = duration > 0 ? duration * 0.1 : 0;

        const extracted =
            (await extractFrame(ffmpegPath, localPath, seek, tempPath)) ||
            (seek > 0 && (await extractFrame(ffmpegPath, localPath, 0, tempPath)));
        if (!extracted) {
            return [];
        }

        // Treat the extracted frame as a PNG so the HD thumbnail is generated.
        const image = new Bun.Image(tempPath, { autoOrient: true });
        const metadata = await image.metadata();
        const width = metadata.width;
        const height = metadata.height;
        if (!width || !height) {
            return [];
        }

        return await thumbnailsForImage(image, width, height);
    } catch {
        return [];
    } finally {
        await unlink(tempPath).catch(() => {});
    }
}

async function thumbnailsForImage(image: Bun.Image, width: number, height: number): Promise<Thumbnail[]> {
    const type1Buffer = await webpFitMaxBytes(image, MAX_THUMBNAIL_SIDE, MAX_THUMBNAIL_BYTES);
    if (!type1Buffer) {
        return [];
    }

    const thumbnails: Thumbnail[] = [
        {
            type: ThumbnailType.Type1,
            thumbnail: new Uint8Array(type1Buffer),
        },
    ];

    // Always generate HD for video thumbnails (they're frames, not original images)
    const type2Buffer = await webpFitMaxBytes(image, MAX_HD_THUMBNAIL_SIDE, MAX_HD_THUMBNAIL_BYTES);
    if (type2Buffer) {
        thumbnails.push({
            type: ThumbnailType.Type2,
            thumbnail: new Uint8Array(type2Buffer),
        });
    }

    return thumbnails;
}

function shouldGenerateHdThumbnail(width: number, height: number, mediaType: string): boolean {
    const mt = mediaType.trim().toLowerCase();
    const isJpeg = mt === 'image/jpeg' || mt === 'image/jpg';
    const isWebp = mt === 'image/webp';
    if (Math.max(width, height) > MAX_HD_THUMBNAIL_SIDE) {
        return true;
    }
    return !isJpeg && !isWebp;
}

async function webpFitMaxBytes(image: Bun.Image, maxSide: number, maxBytes: number): Promise<Buffer | undefined> {
    let quality = 90;
    let out: Buffer | undefined;
    while (quality > 0) {
        try {
            out = await image
                .resize(maxSide, maxSide, { fit: 'inside', withoutEnlargement: true })
                .webp({ quality })
                .buffer();
        } catch {
            return undefined;
        }
        if (out.length <= maxBytes) {
            break;
        }
        if (quality <= 10) {
            return undefined;
        }
        quality -= 20;
    }
    return out;
}

let ffmpegPathCache: string | null | undefined;
function findFfmpeg(): string | null {
    if (ffmpegPathCache === undefined) {
        ffmpegPathCache = Bun.which('ffmpeg');
    }
    return ffmpegPathCache;
}

let ffprobePathCache: string | null | undefined;
function findFfprobe(): string | null {
    if (ffprobePathCache === undefined) {
        ffprobePathCache = Bun.which('ffprobe');
    }
    return ffprobePathCache;
}

let ffmpegMissingWarned = false;
function warnFfmpegMissingOnce(): void {
    if (ffmpegMissingWarned) {
        return;
    }
    ffmpegMissingWarned = true;
    console.warn(
        'Warning: ffmpeg not found — uploading video(s) without a thumbnail. ' +
            'Install ffmpeg to enable video thumbnails, or pass --skip-thumbnails to silence this.',
    );
}

async function probeDuration(ffprobePath: string, localPath: string): Promise<number> {
    try {
        const proc = Bun.spawn({
            cmd: [
                ffprobePath,
                '-v',
                'error',
                '-show_entries',
                'format=duration',
                '-of',
                'default=nw=1:nk=1',
                localPath,
            ],
            stdout: 'pipe',
            stderr: 'ignore',
        });
        const out = await new Response(proc.stdout).text();
        await proc.exited;
        const seconds = parseFloat(out.trim());
        return Number.isFinite(seconds) && seconds > 0 ? seconds : 0;
    } catch {
        return 0;
    }
}

async function extractFrame(ffmpegPath: string, localPath: string, seek: number, outPath: string): Promise<boolean> {
    try {
        const proc = Bun.spawn({
            cmd: [
                ffmpegPath,
                '-nostdin',
                '-y',
                '-ss',
                String(seek),
                '-i',
                localPath,
                '-frames:v',
                '1',
                '-vf',
                'thumbnail',
                '-an',
                '-sn',
                outPath,
            ],
            stdout: 'ignore',
            stderr: 'ignore',
        });
        const exitCode = await proc.exited;
        if (exitCode !== 0) {
            return false;
        }
        const file = Bun.file(outPath);
        return (await file.exists()) && file.size > 0;
    } catch {
        return false;
    }
}
