export async function uploadSelectedFileToR2(inputId, uploadUrl, contentType) {
  const input = document.getElementById(inputId);
  if (!(input instanceof HTMLInputElement) || !input.files || input.files.length === 0) {
    throw new Error("No file selected for upload.");
  }

  const file = input.files[0];
  const resolvedContentType =
    (typeof contentType === "string" && contentType.trim().length > 0 ? contentType.trim() : "") ||
    file.type ||
    "application/octet-stream";

  const response = await fetch(uploadUrl, {
    method: "PUT",
    mode: "cors",
    headers: {
      "Content-Type": resolvedContentType
    },
    body: file
  });

  if (!response.ok) {
    const message = await response.text().catch(() => "");
    throw new Error(
      message && message.trim().length > 0
        ? `Direct upload failed: ${message}`
        : `Direct upload failed with status ${response.status}.`
    );
  }
}
