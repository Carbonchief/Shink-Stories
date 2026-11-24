let quillStyles;
let quillScript;

function loadAsset(element) {
    return new Promise((resolve, reject) => {
        element.onload = resolve;
        element.onerror = () => {
            element.remove();
            reject(new Error("Could not load the rich text editor."));
        };
        document.head.appendChild(element);
    });
}

export async function ensureQuillLoaded() {
    if (!quillStyles) {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = "/lib/quill/quill.snow.css";
        quillStyles = loadAsset(link).catch(error => {
            quillStyles = null;
            throw error;
        });
    }
    if (!window.Quill && !quillScript) {
        const script = document.createElement("script");
        script.src = "/lib/quill/quill.js";
        quillScript = loadAsset(script).catch(error => {
            quillScript = null;
            throw error;
        });
    }
    await Promise.all([quillStyles, quillScript]);
    if (!window.Quill) {
        quillScript = null;
        throw new Error("Could not initialize the rich text editor.");
    }
}

// One buffer per draft or text block. Save and navigation can await the same delivery as the timer.
export function bufferEditorChanges(readHtml, deliver, markDirty) {
    let timer;
    let pending = false;
    let inFlight;
    let firstEdit = true;

    async function flush() {
        clearTimeout(timer);
        while (inFlight) {
            await inFlight;
        }
        if (!pending) return;

        pending = false;
        let stream;
        try {
            const html = readHtml();
            // Long or heavily escaped documents can exceed the SignalR message limit as JSON.
            if (html.length > 4096) stream = DotNet.createJSStreamReference(new Blob([html]));
            inFlight = deliver(stream ? null : html, stream);
            await inFlight;
        } catch (error) {
            pending = true;
            throw error;
        } finally {
            inFlight = null;
            if (stream) DotNet.disposeJSObjectReference(stream);
        }
    }

    return {
        changed() {
            if (firstEdit) {
                firstEdit = false;
                markDirty?.();
            }
            pending = true;
            clearTimeout(timer);
            timer = setTimeout(() => flush().catch(console.error), 300);
        },
        flush,
        cancel() {
            clearTimeout(timer);
            pending = false;
        }
    };
}
