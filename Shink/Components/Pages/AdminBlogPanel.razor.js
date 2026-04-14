const editorStates = new WeakMap();

export function syncRichTextEditor(shellElement, dotNetReference, html) {
    if (!shellElement) {
        return;
    }

    const editor = shellElement.querySelector(".blog-admin-markdown-editor[contenteditable='true']");
    const toolbar = shellElement.querySelector(".blog-admin-rich-toolbar");
    if (!editor) {
        return;
    }

    let state = editorStates.get(shellElement);
    if (!state) {
        state = {
            editor,
            toolbar,
            dotNetReference,
            isApplying: false,
            onInput: null,
            onToolbarClick: null
        };

        state.onInput = () => {
            if (state.isApplying) {
                return;
            }

            const currentHtml = state.editor.innerHTML ?? "";
            state.dotNetReference?.invokeMethodAsync("OnRichTextEditorInput", currentHtml);
        };

        state.onToolbarClick = (event) => {
            const button = event.target?.closest?.("[data-rich-text-command]");
            if (!button || !state.toolbar?.contains(button)) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            applyCommand(state.editor, button.dataset.richTextCommand);
        };

        editor.addEventListener("input", state.onInput);
        editor.addEventListener("blur", state.onInput);
        toolbar?.addEventListener("click", state.onToolbarClick);
        editorStates.set(shellElement, state);
    } else {
        state.editor = editor;
        state.toolbar = toolbar;
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

    if (state.onInput) {
        state.editor.removeEventListener("input", state.onInput);
        state.editor.removeEventListener("blur", state.onInput);
    }

    if (state.onToolbarClick) {
        state.toolbar?.removeEventListener("click", state.onToolbarClick);
    }

    editorStates.delete(shellElement);
}

function setEditorHtml(state, html) {
    if (state.editor.innerHTML === html) {
        return;
    }

    state.isApplying = true;
    state.editor.innerHTML = html;
    state.isApplying = false;
}

function applyCommand(editor, command) {
    editor.focus();

    switch (command) {
        case "bold":
            document.execCommand("bold");
            break;
        case "italic":
            document.execCommand("italic");
            break;
        case "underline":
            document.execCommand("underline");
            break;
        case "heading2":
            document.execCommand("formatBlock", false, "<h2>");
            break;
        case "heading3":
            document.execCommand("formatBlock", false, "<h3>");
            break;
        case "unorderedList":
            document.execCommand("insertUnorderedList");
            break;
        case "orderedList":
            document.execCommand("insertOrderedList");
            break;
        case "clearFormatting":
            document.execCommand("removeFormat");
            document.execCommand("formatBlock", false, "<p>");
            break;
        default:
            break;
    }

    editor.dispatchEvent(new Event("input", { bubbles: true }));
}
