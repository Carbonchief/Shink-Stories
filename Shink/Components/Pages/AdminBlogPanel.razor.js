const editorStates = new WeakMap();
let quillLoaderPromise;

export async function syncRichTextEditor(shellElement, dotNetReference, html) {
    if (!shellElement) {
        return;
    }

    await ensureQuillLoaded();
    registerBlogMediaBlots();

    const editorElement = shellElement.querySelector(".blog-admin-markdown-editor");
    const toolbarElement = shellElement.querySelector(".blog-admin-rich-toolbar");
    if (!editorElement || !toolbarElement) {
        return;
    }

    let state = editorStates.get(shellElement);
    if (!state) {
        const quill = new window.Quill(editorElement, {
            theme: "snow",
            placeholder: editorElement.dataset.placeholder ?? "",
            modules: {
                history: {
                    delay: 500,
                    maxStack: 200,
                    userOnly: true
                },
                toolbar: {
                    container: toolbarElement,
                    handlers: {
                        undo() {
                            this.quill.history.undo();
                        },
                        redo() {
                            this.quill.history.redo();
                        }
                    }
                }
            }
        });

        quill.root.setAttribute("spellcheck", "true");
        quill.root.setAttribute("role", "textbox");
        quill.root.setAttribute("aria-multiline", "true");

        const editorLabel = toolbarElement.dataset.editorLabel ?? editorElement.dataset.placeholder ?? "";
        if (editorLabel) {
            quill.root.setAttribute("aria-label", editorLabel);
        }

        state = {
            dotNetReference,
            isApplying: false,
            lastSelectionIndex: 0,
            onSelectionChange: null,
            onTextChange: null,
            quill
        };

        state.onTextChange = (_delta, _oldDelta, source) => {
            if (state.isApplying || source !== "user") {
                return;
            }

            const currentHtml = getEditorHtml(state.quill);
            state.dotNetReference?.invokeMethodAsync("OnRichTextEditorInput", currentHtml);
        };

        state.onSelectionChange = (range) => {
            if (range) {
                state.lastSelectionIndex = range.index;
            }
        };

        quill.on("text-change", state.onTextChange);
        quill.on("selection-change", state.onSelectionChange);
        editorStates.set(shellElement, state);
    } else {
        state.dotNetReference = dotNetReference;
    }

    setEditorHtml(state, html ?? "");
}

export function disposeRichTextEditor(shellElement) {
    if (!shellElement) {
        return;
    }

    const state = editorStates.get(shellElement);
    if (!state) {
        return;
    }

    if (state.onTextChange) {
        state.quill.off("text-change", state.onTextChange);
    }

    if (state.onSelectionChange) {
        state.quill.off("selection-change", state.onSelectionChange);
    }

    editorStates.delete(shellElement);
}

export async function insertBlogImage(shellElement, url, alt) {
    const state = editorStates.get(shellElement);
    if (!state || !isSafeMediaUrl(url)) {
        throw new Error("The blog editor is not ready for an image.");
    }

    const insertionIndex = resolveInsertionIndex(state);
    state.quill.insertEmbed(
        insertionIndex,
        "blogImage",
        {
            url,
            alt: typeof alt === "string" && alt.trim() ? alt.trim() : "Blog image"
        },
        "user");
    state.quill.insertText(insertionIndex + 1, "\n", "user");
    state.quill.setSelection(insertionIndex + 2, 0, "silent");
    state.lastSelectionIndex = insertionIndex + 2;
    await notifyContentChanged(state);
}

export async function insertBlogVideo(shellElement, provider, url, title) {
    const state = editorStates.get(shellElement);
    if (!state || !isSafeMediaUrl(url)) {
        throw new Error("The blog editor is not ready for a video.");
    }

    const supportedProvider =
        provider === "youtube" ||
        provider === "cloudflare" ||
        provider === "cloudflare-iframe";
    if (!supportedProvider) {
        throw new Error("Unsupported blog video provider.");
    }

    const insertionIndex = resolveInsertionIndex(state);
    state.quill.insertEmbed(
        insertionIndex,
        "blogVideo",
        {
            provider,
            url,
            title: typeof title === "string" && title.trim() ? title.trim() : "Blog video"
        },
        "user");
    state.quill.insertText(insertionIndex + 1, "\n", "user");
    state.quill.setSelection(insertionIndex + 2, 0, "silent");
    state.lastSelectionIndex = insertionIndex + 2;
    await notifyContentChanged(state);
}

export async function uploadSelectedFileToR2(inputId, uploadUrl, contentType) {
    const input = document.getElementById(inputId);
    if (!(input instanceof HTMLInputElement) || !input.files || input.files.length === 0) {
        throw new Error("No file selected for upload.");
    }

    const file = input.files[0];
    const resolvedContentType =
        (typeof contentType === "string" && contentType.trim() ? contentType.trim() : "") ||
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
            message && message.trim()
                ? `Direct upload failed: ${message}`
                : `Direct upload failed with status ${response.status}.`);
    }
}

export function clearFileInput(inputId) {
    const input = document.getElementById(inputId);
    if (input instanceof HTMLInputElement) {
        input.value = "";
    }
}

async function ensureQuillLoaded() {
    if (window.Quill) {
        await ensureQuillStylesheet();
        return window.Quill;
    }

    if (!quillLoaderPromise) {
        quillLoaderPromise = Promise.all([
            ensureQuillStylesheet(),
            ensureQuillScript()
        ]).then(() => {
            if (!window.Quill) {
                throw new Error("Quill did not load correctly.");
            }

            return window.Quill;
        });
    }

    return quillLoaderPromise;
}

function registerBlogMediaBlots() {
    if (window.__shinkBlogMediaBlotsRegistered) {
        return;
    }

    const BlockEmbed = window.Quill.import("blots/block/embed");

    class BlogImageBlot extends BlockEmbed {
        static create(value) {
            const node = super.create();
            const image = document.createElement("img");
            image.src = value?.url ?? "";
            image.alt = value?.alt ?? "Blog image";
            image.loading = "lazy";
            image.decoding = "async";
            node.appendChild(image);
            return node;
        }

        static value(node) {
            const image = node.querySelector("img");
            return {
                url: image?.getAttribute("src") ?? "",
                alt: image?.getAttribute("alt") ?? "Blog image"
            };
        }
    }

    BlogImageBlot.blotName = "blogImage";
    BlogImageBlot.tagName = "figure";
    BlogImageBlot.className = "blog-media-image";

    class BlogVideoBlot extends BlockEmbed {
        static create(value) {
            const node = super.create();
            const provider = value?.provider ?? "cloudflare";
            const title = value?.title ?? "Blog video";
            const url = value?.url ?? "";

            if (provider === "youtube" || provider === "cloudflare-iframe") {
                const frame = document.createElement("iframe");
                frame.src = url;
                frame.title = title;
                frame.loading = "lazy";
                frame.frameBorder = "0";
                frame.referrerPolicy = "strict-origin-when-cross-origin";
                frame.setAttribute(
                    "allow",
                    "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share");
                frame.setAttribute("allowfullscreen", "");
                node.classList.add(
                    provider === "youtube" ? "blog-media-youtube" : "blog-media-cloudflare");
                node.appendChild(frame);
                return node;
            }

            const video = document.createElement("video");
            video.src = url;
            video.title = title;
            video.controls = true;
            video.playsInline = true;
            video.preload = "metadata";
            node.classList.add("blog-media-cloudflare");
            node.appendChild(video);
            return node;
        }

        static value(node) {
            const frame = node.querySelector("iframe");
            if (frame) {
                return {
                    provider: node.classList.contains("blog-media-youtube")
                        ? "youtube"
                        : "cloudflare-iframe",
                    url: frame.getAttribute("src") ?? "",
                    title: frame.getAttribute("title") ?? "Blog video"
                };
            }

            const video = node.querySelector("video");
            return {
                provider: "cloudflare",
                url: video?.getAttribute("src") ?? "",
                title: video?.getAttribute("title") ?? "Blog video"
            };
        }
    }

    BlogVideoBlot.blotName = "blogVideo";
    BlogVideoBlot.tagName = "figure";
    BlogVideoBlot.className = "blog-media-video";

    window.Quill.register(BlogImageBlot, true);
    window.Quill.register(BlogVideoBlot, true);
    window.__shinkBlogMediaBlotsRegistered = true;
}

function ensureQuillStylesheet() {
    const existing = document.querySelector("link[data-blog-admin-quill-styles='true']");
    if (existing) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = "/lib/quill/quill.snow.css";
        link.dataset.blogAdminQuillStyles = "true";
        link.addEventListener("load", () => resolve(), { once: true });
        link.addEventListener("error", () => reject(new Error("Failed to load Quill stylesheet.")), { once: true });
        document.head.appendChild(link);
    });
}

function ensureQuillScript() {
    if (window.Quill) {
        return Promise.resolve(window.Quill);
    }

    const existing = document.querySelector("script[data-blog-admin-quill-script='true']");
    if (existing) {
        return new Promise((resolve, reject) => {
            if (existing.dataset.loaded === "true") {
                resolve(window.Quill);
                return;
            }

            existing.addEventListener("load", () => resolve(window.Quill), { once: true });
            existing.addEventListener("error", () => reject(new Error("Failed to load Quill script.")), { once: true });
        });
    }

    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "/lib/quill/quill.js";
        script.async = true;
        script.dataset.blogAdminQuillScript = "true";
        script.addEventListener("load", () => {
            script.dataset.loaded = "true";
            resolve(window.Quill);
        }, { once: true });
        script.addEventListener("error", () => reject(new Error("Failed to load Quill script.")), { once: true });
        document.head.appendChild(script);
    });
}

function setEditorHtml(state, html) {
    const normalizedHtml = normalizeHtml(html);
    if (getEditorHtml(state.quill) === normalizedHtml) {
        return;
    }

    state.isApplying = true;

    if (normalizedHtml) {
        state.quill.clipboard.dangerouslyPasteHTML(normalizedHtml, "silent");
    } else {
        state.quill.setText("", "silent");
    }

    state.quill.history.clear();
    state.isApplying = false;
}

function getEditorHtml(quill) {
    if (!quill || quill.getLength() <= 1) {
        return "";
    }

    const exportedHtml = typeof quill.getSemanticHTML === "function"
        ? quill.getSemanticHTML()
        : quill.root.innerHTML;

    return normalizeHtml(exportedHtml);
}

function resolveInsertionIndex(state) {
    const currentRange = state.quill.getSelection();
    const requestedIndex = currentRange?.index ?? state.lastSelectionIndex ?? state.quill.getLength() - 1;
    return Math.max(0, Math.min(requestedIndex, state.quill.getLength() - 1));
}

async function notifyContentChanged(state) {
    const currentHtml = getEditorHtml(state.quill);
    await state.dotNetReference?.invokeMethodAsync("OnRichTextEditorInput", currentHtml);
}

function isSafeMediaUrl(value) {
    if (typeof value !== "string" || !value.trim()) {
        return false;
    }

    try {
        const url = new URL(value, window.location.origin);
        return url.protocol === "https:" ||
            (url.protocol === "http:" && url.origin === window.location.origin) ||
            (url.origin === window.location.origin && value.startsWith("/"));
    } catch {
        return false;
    }
}

function normalizeHtml(html) {
    const trimmed = (html ?? "").trim();
    if (!trimmed || /^<p>(?:<br\s*\/?>|&nbsp;|\s)*<\/p>$/i.test(trimmed)) {
        return "";
    }

    const container = document.createElement("div");
    container.innerHTML = trimmed;

    if (/^<p>(?:<br\s*\/?>|&nbsp;|\s)*<\/p>$/i.test(container.innerHTML.trim())) {
        return "";
    }

    return container.innerHTML.trim();
}
