import { CART_KEY, readState, updateBadges } from "/Components/Store/StoreBasket.razor.js";

let store;
let quantityScript;
let navigationBound = false;

async function refresh(force = false) {
    const root = document.querySelector("[data-store]");
    if (!root) {
        store?.initialize();
        updateBadges(readState().items);
        return;
    }

    if (!quantityScript && !window.schinkWinkelQuantityReady &&
        !Array.from(document.scripts).some(script => script.src === new URL(root.dataset.quantityScript, document.baseURI).href)) {
        quantityScript = document.createElement("script");
        quantityScript.src = root.dataset.quantityScript;
        document.head.appendChild(quantityScript);
    }
    store = await import(new URL(root.dataset.storeModule, document.baseURI).href);
    if (root.isConnected) store.initialize(force);
}

function bindNavigation() {
    if (!navigationBound && window.Blazor?.addEventListener) {
        window.Blazor.addEventListener("enhancedload", () => refresh(true));
        navigationBound = true;
    }
}

bindNavigation();
window.addEventListener("schink:blazor-ready", bindNavigation);
window.addEventListener("pageshow", event => { if (event.persisted) refresh(true); });
window.addEventListener("storage", event => {
    if ((!event.key || event.key === CART_KEY) && !document.querySelector("[data-store]")) updateBadges(readState().items);
});
refresh();
