const editorStates = new WeakMap();
let quillLoaderPromise;

export async function syncRichTextEditor(shellElement, dotNetReference, html) {
    if (!shellElement) {
        return;
    }

    await ensureQuillLoaded();

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

        quill.on("text-change", state.onTextChange);
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

    editorStates.delete(shellElement);
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
