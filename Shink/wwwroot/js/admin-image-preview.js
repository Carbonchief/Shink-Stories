export async function createThumbnail(inputId) {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file) return null;

    const bitmap = typeof createImageBitmap === "function" ? await createImageBitmap(file) : await readImage(file);
    try {
        const scale = Math.min(1, 480 / Math.max(bitmap.width, bitmap.height));
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(bitmap.width * scale));
        canvas.height = Math.max(1, Math.round(bitmap.height * scale));
        canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
        return await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
    } finally {
        bitmap.close?.();
    }
}

async function readImage(file) {
    // Older browsers can decode a local data URL without sending the file to the server.
    const source = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(file);
    });
    const image = new Image();
    image.src = source;
    await image.decode();
    return image;
}
