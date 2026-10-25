// Phone photos are 2-5 MB, and sending one from a site's mobile connection was most of the time
// a submission with a photo took (measured 2026-10-26: a 2 MB upload 1.9-2.4 s, of which the
// server's own work ~0.5 s). Scaled to 1920 px on the long side, a photo is a few hundred KB and
// still clear on screen. Anything that can't be decoded here (HEIC outside Safari, PDFs) or
// wouldn't get smaller is sent unchanged.

const MAX_DIMENSION = 1920;
const JPEG_QUALITY = 0.82;
const MIN_BYTES_WORTH_SHRINKING = 300 * 1024;
const SHRINKABLE_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp', 'image/heic']);

export async function shrinkImageForUpload(file: File): Promise<File> {
  if (!SHRINKABLE_TYPES.has(file.type.toLowerCase()) || file.size < MIN_BYTES_WORTH_SHRINKING) return file;

  try {
    // 'from-image' applies the camera's EXIF rotation, so portrait photos stay upright.
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    try {
      const scale = Math.min(1, MAX_DIMENSION / Math.max(bitmap.width, bitmap.height));
      const width = Math.round(bitmap.width * scale);
      const height = Math.round(bitmap.height * scale);

      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) return file;
      // JPEG has no transparency; without a fill, transparent PNG areas turn black.
      context.fillStyle = '#ffffff';
      context.fillRect(0, 0, width, height);
      context.drawImage(bitmap, 0, 0, width, height);

      const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
      if (!blob || blob.size >= file.size) return file;

      const name = `${file.name.replace(/\.[^.]*$/, '') || 'photo'}.jpg`;
      return new File([blob], name, { type: 'image/jpeg', lastModified: file.lastModified });
    } finally {
      bitmap.close();
    }
  } catch {
    return file;
  }
}
