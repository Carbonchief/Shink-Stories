import { initializeHomeStoryLoading } from "/Components/Pages/Home.razor.js";

const bindings = new WeakMap();

export function initializeLuisterPage(input, component) {
    initializeHomeStoryLoading();
    if (!input || bindings.has(input)) return;

    input.dataset.searchReady = "true";
    const main = input.closest("main");
    const pending = main?.dataset.pendingLuisterSearch;
    if (pending !== undefined) {
        input.value = pending;
        delete main.dataset.pendingLuisterSearch;
        if (pending && document.activeElement === document.body) input.focus({ preventScroll: true });
    }
    const indicator = input.form.querySelector(".luister-search-loader");
    let timer;
    let revision = 0;
    const submit = async (version) => {
        if (!input.isConnected) return;
        try {
            await component.invokeMethodAsync("ApplySearchAsync", input.value);
        } catch (error) {
            if (input.isConnected && bindings.has(input)) console.debug("Luister search interrupted", error);
        } finally {
            if (version === revision) indicator.hidden = true;
        }
    };
    const onInput = () => {
        clearTimeout(timer);
        const version = ++revision;
        indicator.hidden = !input.value.trim();
        timer = setTimeout(() => submit(version), indicator.hidden ? 0 : 180);
    };
    input.addEventListener("input", onInput);
    bindings.set(input, () => {
        clearTimeout(timer);
        input.removeEventListener("input", onInput);
    });
    // A visitor can already type into the prerendered field while Blazor connects.
    if (input.value) onInput();
}

export function disposeLuisterPage(input) {
    if (!input) return;
    bindings.get(input)?.();
    bindings.delete(input);
}
