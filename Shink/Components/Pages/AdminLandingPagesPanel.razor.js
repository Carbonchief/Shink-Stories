const editorStates = new WeakMap();
let quillLoaderPromise;

const allowedTags = new Set(["P", "H2", "H3", "STRONG", "EM", "A", "OL", "UL", "LI", "BR"]);
const allowedFormats = ["header", "bold", "italic", "link", "list"];

export async function syncRichTextEditor(shellElement, dotNetReference, html, disabled = false) {
    if (!shellElement) {
        return;
    }

    await ensureQuillLoaded();

    const editorElement = shellElement.querySelector(".landing-rich-editor");
    const toolbarElement = shellElement.querySelector(".landing-rich-toolbar");
    if (!editorElement || !toolbarElement) {
        return;
    }

    let state = editorStates.get(shellElement);
    if (!state) {
        toolbarElement.classList.add("ql-snow");
        const quill = new window.Quill(editorElement, {
            theme: "snow",
            formats: allowedFormats,
            placeholder: editorElement.dataset.placeholder ?? "",
            modules: {
                history: {
                    delay: 500,
                    maxStack: 200,
                    userOnly: true
                },
                toolbar: {
                    container: toolbarElement
                }
            }
        });

        quill.root.setAttribute("spellcheck", "true");
        quill.root.setAttribute("role", "textbox");
        quill.root.setAttribute("aria-multiline", "true");

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

            state.dotNetReference?.invokeMethodAsync("OnLandingRichTextEditorInput", getEditorHtml(state.quill));
        };
        quill.on("text-change", state.onTextChange);
        editorStates.set(shellElement, state);
    } else {
        state.dotNetReference = dotNetReference;
    }

    const editorLabel = toolbarElement.dataset.editorLabel ?? editorElement.dataset.placeholder ?? "";
    if (editorLabel) {
        state.quill.root.setAttribute("aria-label", editorLabel);
    } else {
        state.quill.root.removeAttribute("aria-label");
    }

    state.quill.enable(!disabled);
    setEditorHtml(state, html ?? "");
}

export function setRichTextEditorDisabled(shellElement, disabled) {
    const state = editorStates.get(shellElement);
    if (state) {
        state.quill.enable(!disabled);
    }
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

function getEditorHtml(quill) {
    if (!quill || quill.getLength() <= 1) {
        return "";
    }

    return sanitizeSemanticHtml(quill.root.innerHTML);
}

function sanitizeSemanticHtml(value) {
    const template = document.createElement("template");
    template.innerHTML = typeof value === "string" ? value : "";

    for (const list of Array.from(template.content.querySelectorAll("ol, ul"))) {
        const items = Array.from(list.children).filter(child => child.tagName === "LI");
        if (list.tagName !== "OL" || items.length === 0 || !items.some(item => item.hasAttribute("data-list"))) {
            continue;
        }

        const groups = [];
        for (const item of items) {
            const listType = item.getAttribute("data-list") === "bullet" ? "ul" : "ol";
            let group = groups.at(-1);
            if (!group || group.tagName.toLowerCase() !== listType) {
                group = document.createElement(listType);
                groups.push(group);
            }
            group.appendChild(item);
        }

        list.replaceWith(...groups);
    }

    for (const element of Array.from(template.content.querySelectorAll("*"))) {
        if (!allowedTags.has(element.tagName)) {
            if (["SCRIPT", "STYLE", "IFRAME", "OBJECT", "EMBED", "SVG", "MATH", "IMG"].includes(element.tagName)) {
                element.remove();
            } else {
                element.replaceWith(...Array.from(element.childNodes));
            }
            continue;
        }

        if (element.tagName === "A") {
            const safeHref = normalizeSafeHref(element.getAttribute("href"));
            if (safeHref) {
                element.setAttribute("href", safeHref);
            } else {
                element.replaceWith(...Array.from(element.childNodes));
                continue;
            }
        }

        for (const attribute of Array.from(element.attributes)) {
            if (!(element.tagName === "A" && attribute.name === "href")) {
                element.removeAttribute(attribute.name);
            }
        }
    }

    return template.innerHTML;
}

function normalizeSafeHref(value) {
    const rawValue = typeof value === "string" ? value : "";
    if (!rawValue || /[\u0000-\u001f\u007f]/.test(rawValue) || rawValue.includes("\\")) {
        return null;
    }

    const candidate = rawValue.trim();
    if (!candidate) {
        return null;
    }

    if (candidate.startsWith("/") && !candidate.startsWith("//")) {
        const path = candidate.split(/[?#]/, 1)[0];
        let decodedPath;
        try {
            decodedPath = decodeURIComponent(path);
        } catch {
            return null;
        }

        if (decodedPath.split("/").some(segment => segment === "..")) {
            return null;
        }
        return candidate;
    }
    if (candidate.startsWith("#") || candidate.startsWith("?")) {
        return candidate;
    }

    try {
        const uri = new URL(candidate);
        return uri.protocol === "https:" && uri.hostname && !uri.username && !uri.password
            ? candidate
            : null;
    } catch {
        return null;
    }
}

function setEditorHtml(state, html) {
    const normalizedHtml = sanitizeSemanticHtml(html);
    if (getEditorHtml(state.quill) === normalizedHtml) {
        return;
    }

    state.isApplying = true;
    try {
        state.quill.setText("", "silent");
        if (normalizedHtml) {
            state.quill.clipboard.dangerouslyPasteHTML(normalizedHtml, "silent");
        }
        state.quill.history.clear();
    } finally {
        state.isApplying = false;
    }
}

async function ensureQuillLoaded() {
    if (window.Quill) {
        await ensureQuillStylesheet();
        return window.Quill;
    }

    if (!quillLoaderPromise) {
        quillLoaderPromise = Promise.all([ensureQuillStylesheet(), ensureQuillScript()])
            .then(() => {
                if (!window.Quill) {
                    throw new Error("Quill did not load correctly.");
                }
                return window.Quill;
            })
            .catch(error => {
                quillLoaderPromise = null;
                throw error;
            });
    }

    return quillLoaderPromise;
}

function ensureQuillStylesheet() {
    if (document.querySelector("link[data-landing-admin-quill-styles='true']")) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = "/lib/quill/quill.snow.css";
        link.dataset.landingAdminQuillStyles = "true";
        link.onload = () => resolve();
        link.onerror = () => {
            link.remove();
            reject(new Error("Quill styles could not be loaded."));
        };
        document.head.appendChild(link);
    });
}

function ensureQuillScript() {
    if (window.Quill) {
        return Promise.resolve();
    }

    const existing = document.querySelector("script[data-landing-admin-quill-script='true']");
    if (existing) {
        return new Promise((resolve, reject) => {
            existing.addEventListener("load", () => resolve(), { once: true });
            existing.addEventListener("error", () => reject(new Error("Quill script could not be loaded.")), { once: true });
        });
    }

    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "/lib/quill/quill.js";
        script.dataset.landingAdminQuillScript = "true";
        script.onload = () => resolve();
        script.onerror = () => {
            script.remove();
            reject(new Error("Quill script could not be loaded."));
        };
        document.head.appendChild(script);
    });
}

export async function copyPageUrl(url) {
    try {
        await navigator.clipboard.writeText(url);
        return;
    } catch {
        const field = document.createElement("textarea");
        field.value = url;
        field.style.position = "fixed";
        field.style.opacity = "0";
        document.body.appendChild(field);
        const previousFocus = document.activeElement;
        try {
            field.select();
            if (!document.execCommand("copy")) throw new Error("Clipboard unavailable");
        } finally {
            field.remove();
            previousFocus?.focus();
        }
    }
}
