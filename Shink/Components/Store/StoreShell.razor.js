const CART_KEY = "schink.store.cart.v1";
const DRAFT_KEY = "schink.store.checkout.v1";
const COOKIE_KEY = "shink.store.cart";
const MAX_PER_PRODUCT = 10;
const MAX_ITEMS = 20;

export function normalizeCart(input, products) {
    const known = new Set(products.map((product) => product.slug));
    const quantities = new Map();
    let remaining = MAX_ITEMS;
    for (const item of Array.isArray(input) ? input.slice(0, 500) : []) {
        if (
            !item ||
            !known.has(item.slug) ||
            !Number.isInteger(item.quantity) ||
            item.quantity <= 0
        )
            continue;
        const previous = quantities.get(item.slug) || 0;
        const added = Math.min(
            item.quantity,
            MAX_PER_PRODUCT - previous,
            remaining,
        );
        if (added > 0) quantities.set(item.slug, previous + added);
        remaining -= added;
    }
    return [...quantities].map(([slug, quantity]) => ({ slug, quantity }));
}

export function calculateTotals(items, products, deliveryCents = 8000) {
    const catalogue = new Map(
        products.map((product) => [product.slug, product]),
    );
    let subtotal = 0;
    let count = 0;
    let deliveryIncluded = false;
    for (const item of normalizeCart(items, products)) {
        const product = catalogue.get(item.slug);
        subtotal += product.price * item.quantity;
        count += item.quantity;
        deliveryIncluded = deliveryIncluded || product.freeDelivery;
    }
    const delivery = count > 0 && !deliveryIncluded ? deliveryCents : 0;
    return {
        subtotal,
        count,
        delivery,
        deliveryIncluded,
        total: subtotal + delivery,
    };
}

export function changeQuantity(items, slug, quantity, products) {
    const current = normalizeCart(items, products);
    if (
        !products.some((product) => product.slug === slug) ||
        !Number.isInteger(quantity) ||
        quantity < 0 ||
        quantity > MAX_PER_PRODUCT
    ) {
        return {
            items: current,
            error: "Kies 'n hoeveelheid van 1 tot 10, of verwyder die produk.",
        };
    }
    const updated = current.filter((item) => item.slug !== slug);
    if (quantity > 0) updated.push({ slug, quantity });
    if (updated.reduce((sum, item) => sum + item.quantity, 0) > MAX_ITEMS) {
        return {
            items: current,
            error: "Jy kan tot 20 items in een bestelling plaas.",
        };
    }
    return { items: updated, error: null };
}

export function captureCheckout(state, checkoutId, products) {
    return {
        ...state,
        pendingCheckouts: [
            ...(state.pendingCheckouts || []).filter(
                (checkout) => checkout.id !== checkoutId,
            ),
            { id: checkoutId, items: normalizeCart(state.items, products) },
        ].slice(-20),
    };
}

// Only quantities that have stayed in the basket may be consumed by an older checkout.
// Retired quantities never come back when the shopper rebuilds the basket.
export function replaceCartItems(state, items) {
    const quantities = new Map(items.map((item) => [item.slug, item.quantity]));
    return {
        ...state,
        items,
        pendingCheckouts: (state.pendingCheckouts || []).map((checkout) => ({
            ...checkout,
            items: checkout.items
                .map((item) => ({
                    slug: item.slug,
                    quantity: Math.min(
                        item.quantity,
                        quantities.get(item.slug) || 0,
                    ),
                }))
                .filter((item) => item.quantity > 0),
        })),
    };
}

export function completePurchase(
    state,
    reference,
    purchased,
    products,
    checkoutId,
) {
    if (!reference || (state.completedReferences || []).includes(reference))
        return state;
    const pending = (state.pendingCheckouts || []).find(
        (checkout) => checkout.id === checkoutId,
    );
    const eligible = new Map(
        normalizeCart(pending?.items, products).map((item) => [
            item.slug,
            item.quantity,
        ]),
    );
    const quantities = new Map(
        purchased.map((item) => [item.slug, item.quantity]),
    );
    const remaining = normalizeCart(state.items, products)
        .map((item) => ({
            slug: item.slug,
            quantity: Math.max(
                0,
                item.quantity -
                    Math.min(
                        eligible.get(item.slug) || 0,
                        quantities.get(item.slug) || 0,
                    ),
            ),
        }))
        .filter((item) => item.quantity > 0);
    return replaceCartItems(
        {
            ...state,
            pendingCheckouts: (state.pendingCheckouts || []).filter(
                (checkout) => checkout.id !== checkoutId,
            ),
            completedReferences: [
                ...(state.completedReferences || []),
                reference,
            ].slice(-50),
        },
        remaining,
    );
}

function parseState(value) {
    try {
        const parsed = JSON.parse(value || "null");
        return {
            items: Array.isArray(parsed?.items) ? parsed.items : [],
            updatedAt: Number.isFinite(parsed?.updatedAt)
                ? parsed.updatedAt
                : 0,
            completedReferences: Array.isArray(parsed?.completedReferences)
                ? parsed.completedReferences
                      .filter((value) => typeof value === "string")
                      .slice(-50)
                : [],
            pendingCheckouts: Array.isArray(parsed?.pendingCheckouts)
                ? parsed.pendingCheckouts
                      .filter(
                          (checkout) =>
                              typeof checkout?.id === "string" &&
                              /^[a-f0-9]{32}$/.test(checkout.id) &&
                              Array.isArray(checkout.items),
                      )
                      .slice(-20)
                      .map((checkout) => ({
                          id: checkout.id,
                          items: checkout.items
                              .slice(0, 20)
                              .filter(
                                  (item) =>
                                      typeof item?.slug === "string" &&
                                      Number.isInteger(item.quantity) &&
                                      item.quantity > 0 &&
                                      item.quantity <= MAX_PER_PRODUCT,
                              ),
                      }))
                : [],
        };
    } catch {
        return {
            items: [],
            completedReferences: [],
            pendingCheckouts: [],
            updatedAt: 0,
        };
    }
}

function readState() {
    let local;
    let session;
    try {
        local = localStorage.getItem(CART_KEY);
    } catch {
        /* Optional storage. */
    }
    try {
        session = sessionStorage.getItem(CART_KEY);
    } catch {
        /* Optional storage. */
    }
    if (local || session) {
        const localState = parseState(local),
            sessionState = parseState(session);
        return sessionState.updatedAt > localState.updatedAt
            ? sessionState
            : localState;
    }
    const cookie = document.cookie
        .split("; ")
        .find((value) => value.startsWith(`${COOKIE_KEY}=`));
    try {
        return parseState(
            decodeURIComponent(cookie?.slice(COOKIE_KEY.length + 1) || ""),
        );
    } catch {
        return parseState(null);
    }
}

function saveState(state) {
    state.updatedAt = Math.max(Date.now(), (state.updatedAt || 0) + 1);
    const value = JSON.stringify(state);
    let saved = false;
    try {
        localStorage.setItem(CART_KEY, value);
        saved = true;
    } catch {
        /* Private browsing fallback. */
    }
    try {
        sessionStorage.setItem(CART_KEY, value);
        saved = true;
    } catch {
        /* Cookie fallback. */
    }
    if (saved) return;
    document.cookie = `${COOKIE_KEY}=${encodeURIComponent(value)}; Path=/; Max-Age=2592000; SameSite=Lax${location.protocol === "https:" ? "; Secure" : ""}`;
}

const money = (cents) => `R ${(cents / 100).toFixed(2)}`;
const all = (root, selector) => [...root.querySelectorAll(selector)];
function textAll(root, selector, text) {
    all(root, selector).forEach((element) => {
        element.textContent = text;
    });
}

function updateBadges(items) {
    const count = items.reduce(
        (sum, item) =>
            sum +
            (Number.isInteger(item?.quantity)
                ? Math.max(0, Math.min(MAX_PER_PRODUCT, item.quantity))
                : 0),
        0,
    );
    textAll(document, "[data-store-count]", String(Math.min(MAX_ITEMS, count)));
    all(document, "[data-store-cart-link]").forEach((link) =>
        link.setAttribute(
            "aria-label",
            `Mandjie met ${count} ${count === 1 ? "item" : "items"}`,
        ),
    );
}

let currentRoot;
let currentView;
let disposeCurrent;
let enhancedNavigationBound = false;

function viewKey(root) {
    return `${location.pathname}${location.search}:${root?.dataset.storeView || ""}:${root?.dataset.storeCompletedReference || ""}`;
}

function bindEnhancedNavigation() {
    if (!enhancedNavigationBound && window.Blazor?.addEventListener) {
        window.Blazor.addEventListener("enhancedload", () => {
            currentRoot = null;
            initialize();
        });
        enhancedNavigationBound = true;
    }
}

export function initialize() {
    const root = document.querySelector("[data-store]");
    const key = viewKey(root);
    if (root && currentRoot === root && currentView === key) return;
    disposeCurrent?.();
    currentRoot = root;
    currentView = key;
    let state = readState();
    updateBadges(state.items);
    if (!root) return;

    const controller = new AbortController();
    const listen = (target, event, handler) =>
        target.addEventListener(event, handler, { signal: controller.signal });
    disposeCurrent = () => controller.abort();
    const products = all(root, "[data-store-product]").map((element) => ({
        slug: element.dataset.storeProduct,
        name: element.dataset.name,
        price: Number(element.dataset.price),
        freeDelivery: element.dataset.freeDelivery === "true",
    }));
    const originalItems = JSON.stringify(state.items);
    state = replaceCartItems(state, normalizeCart(state.items, products));
    const completedReference = root.dataset.storeCompletedReference;
    const completedCheckoutId = root.dataset.storeCompletedCheckoutId;
    if (completedReference) {
        state = completePurchase(
            state,
            completedReference,
            all(root, "[data-store-purchased]").map((element) => ({
                slug: element.dataset.storePurchased,
                quantity: Number(element.dataset.quantity),
            })),
            products,
            completedCheckoutId,
        );
        try {
            const draft = JSON.parse(
                sessionStorage.getItem(DRAFT_KEY) || "null",
            );
            if (
                completedCheckoutId &&
                draft?.checkoutId === completedCheckoutId
            )
                sessionStorage.removeItem(DRAFT_KEY);
        } catch {
            /* Storage may be disabled. */
        }
    }
    saveState(state);

    const notice = (message) => {
        const element = root.querySelector("[data-store-notice]");
        if (!element) return;
        element.textContent = message;
        element.hidden = !message;
    };

    function render() {
        const totals = calculateTotals(
            state.items,
            products,
            Number(root.dataset.storeDeliveryCents),
        );
        const quantities = new Map(
            state.items.map((item) => [item.slug, item.quantity]),
        );
        updateBadges(state.items);
        textAll(root, "[data-store-subtotal]", money(totals.subtotal));
        textAll(root, "[data-store-total]", money(totals.total));
        textAll(
            root,
            "[data-store-delivery]",
            totals.deliveryIncluded ? "Ingesluit" : money(totals.delivery),
        );
        textAll(
            root,
            "[data-store-delivery-message]",
            totals.deliveryIncluded
                ? "'n Produk in jou mandjie sluit aflewering vir die hele bestelling in."
                : "PUDO-aflewering word een keer per bestelling bereken.",
        );
        all(root, "[data-store-loading]").forEach((element) => {
            element.hidden = true;
        });
        all(root, "[data-store-empty]").forEach((element) => {
            element.hidden = totals.count > 0;
        });
        all(root, "[data-store-filled]").forEach((element) => {
            element.hidden = totals.count === 0;
        });
        all(root, "[data-store-cart-item], [data-store-summary-item]").forEach(
            (row) => {
                const slug =
                    row.dataset.storeCartItem || row.dataset.storeSummaryItem;
                const quantity = quantities.get(slug) || 0;
                row.hidden = quantity === 0;
                const product = products.find(
                    (product) => product.slug === slug,
                );
                textAll(
                    row,
                    "[data-store-line-total]",
                    money(product.price * quantity),
                );
                textAll(row, "[data-store-item-count]", String(quantity));
                const input = row.querySelector("[data-store-quantity]");
                if (input) input.value = String(quantity);
                const decrement = row.querySelector("[data-store-decrement]");
                if (decrement) decrement.disabled = quantity <= 1;
                const increment = row.querySelector("[data-store-increment]");
                if (increment)
                    increment.disabled =
                        quantity >= MAX_PER_PRODUCT ||
                        totals.count >= MAX_ITEMS;
            },
        );
        all(root, "[data-store-in-cart]").forEach((element) => {
            const quantity = quantities.get(element.dataset.storeInCart) || 0;
            element.hidden = quantity === 0;
            element.textContent = `${quantity} reeds in jou mandjie`;
        });
        all(root, "[data-store-add]").forEach((button) => {
            const quantity = quantities.get(button.dataset.storeAdd) || 0;
            button.disabled =
                quantity >= MAX_PER_PRODUCT || totals.count >= MAX_ITEMS;
        });
        all(root, "[data-store-post-quantity]").forEach((input) => {
            input.value = String(
                quantities.get(input.dataset.storePostQuantity) || 0,
            );
        });
        const postedTotal = root.querySelector("[data-store-post-total]");
        if (postedTotal) postedTotal.value = String(totals.total);
        const pay = root.querySelector("[data-store-pay]");
        if (pay) pay.disabled = totals.count === 0;
    }

    function change(slug, quantity, successMessage) {
        // Re-read before every mutation so another tab's latest changes are preserved.
        state = readState();
        const result = changeQuantity(state.items, slug, quantity, products);
        state = replaceCartItems(state, result.items);
        saveState(state);
        render();
        notice(result.error || successMessage || "Jou mandjie is opgedateer.");
    }

    listen(root, "click", (event) => {
        const button = event.target.closest("button");
        if (!button) return;
        if (button.hasAttribute("data-store-add")) {
            const slug = button.dataset.storeAdd;
            const product = products.find((product) => product.slug === slug);
            const quantityInput = button
                .closest(".winkel-detail-copy")
                ?.querySelector("[data-store-add-quantity]");
            const quantity = quantityInput ? Number(quantityInput.value) : 1;
            const existing =
                normalizeCart(readState().items, products).find(
                    (item) => item.slug === slug,
                )?.quantity || 0;
            if (!Number.isInteger(quantity) || quantity < 1) {
                notice("Kies 'n hoeveelheid van 1 tot 10.");
                return;
            }
            change(
                slug,
                existing + quantity,
                `${product.name} is by jou mandjie. Gaan na jou mandjie wanneer jy gereed is.`,
            );
        } else if (button.hasAttribute("data-store-remove")) {
            change(
                button.dataset.storeRemove,
                0,
                "Die produk is uit jou mandjie verwyder.",
            );
        } else if (
            button.hasAttribute("data-store-increment") ||
            button.hasAttribute("data-store-decrement")
        ) {
            const slug =
                button.dataset.storeIncrement || button.dataset.storeDecrement;
            const quantity =
                normalizeCart(readState().items, products).find(
                    (item) => item.slug === slug,
                )?.quantity || 0;
            change(
                slug,
                quantity +
                    (button.hasAttribute("data-store-increment") ? 1 : -1),
            );
        } else if (button.hasAttribute("data-store-clear")) {
            state = replaceCartItems(readState(), []);
            saveState(state);
            render();
            notice("Jou mandjie is nou leeg.");
        }
    });
    listen(root, "change", (event) => {
        if (event.target.hasAttribute("data-store-quantity"))
            change(
                event.target.dataset.storeQuantity,
                Number(event.target.value),
            );
    });

    const filterProducts = () => {
        const query =
            root
                .querySelector("[data-store-search]")
                ?.value.trim()
                .toLocaleLowerCase("af-ZA") || "";
        const freeOnly = root.querySelector(
            "[data-store-free-filter]",
        )?.checked;
        const sort = root.querySelector("[data-store-sort]")?.value;
        const cards = all(root, "[data-store-card]");
        cards.sort((a, b) =>
            sort === "price-asc"
                ? Number(a.dataset.price) - Number(b.dataset.price)
                : sort === "price-desc"
                  ? Number(b.dataset.price) - Number(a.dataset.price)
                  : sort === "name"
                    ? a.dataset.search.localeCompare(b.dataset.search, "af")
                    : Number(a.dataset.order) - Number(b.dataset.order) ||
                      a.dataset.search.localeCompare(b.dataset.search, "af"),
        );
        let count = 0;
        cards.forEach((card) => {
            card.hidden =
                !card.dataset.search
                    .toLocaleLowerCase("af-ZA")
                    .includes(query) ||
                (freeOnly && card.dataset.freeDelivery !== "true");
            if (!card.hidden) count++;
            card.parentElement.appendChild(card);
        });
        textAll(
            root,
            "[data-store-results]",
            `${count} ${count === 1 ? "produk" : "produkte"}`,
        );
        const empty = root.querySelector("[data-store-no-results]");
        if (empty) empty.hidden = count !== 0;
    };
    const filters = root.querySelector("[data-store-filters]");
    if (filters) {
        listen(filters, "input", filterProducts);
        listen(
            root.querySelector("[data-store-reset-filters]"),
            "click",
            () => {
                root.querySelector("[data-store-search]").value = "";
                root.querySelector("[data-store-free-filter]").checked = false;
                root.querySelector("[data-store-sort]").value = "featured";
                filterProducts();
            },
        );
    }

    const form = root.querySelector("#winkel-order-form");
    if (form) {
        let draftCheckoutId = null;
        const fields = () =>
            form.querySelectorAll(
                "input:not([type=hidden]):not([type=checkbox]), textarea",
            );
        const saveDraft = () => {
            const draft = { checkoutId: draftCheckoutId };
            for (const input of fields()) draft[input.name] = input.value;
            try {
                sessionStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
            } catch {
                /* Optional persistence. */
            }
        };
        delete form.dataset.submitting;
        form.querySelector("[data-store-pay]")?.removeAttribute("aria-busy");
        try {
            const draft = JSON.parse(sessionStorage.getItem(DRAFT_KEY) || "{}");
            draftCheckoutId = draft.checkoutId || null;
            for (const input of fields()) {
                if (typeof draft[input.name] === "string")
                    input.value = draft[input.name];
            }
        } catch {
            /* Invalid or unavailable draft storage does not block checkout. */
        }
        const saveEditedDraft = (event) => {
            if (
                event.target.matches(
                    "input:not([type=hidden]):not([type=checkbox]), textarea",
                )
            ) {
                draftCheckoutId = null;
                saveDraft();
            }
        };
        listen(form, "input", saveEditedDraft);
        listen(form, "change", saveEditedDraft);
        listen(document, "click", (event) => {
            if (event.target.closest("a[href]")) saveDraft();
        });
        listen(window, "pagehide", saveDraft);
        listen(form, "submit", (event) => {
            if (form.dataset.submitting === "true") {
                event.preventDefault();
                return;
            }
            const latestState = readState();
            const latest = normalizeCart(latestState.items, products);
            if (JSON.stringify(latest) !== JSON.stringify(state.items)) {
                event.preventDefault();
                state = replaceCartItems(latestState, latest);
                render();
                notice(
                    "Jou mandjie het in 'n ander oortjie verander. Gaan jou bestelling na en betaal weer.",
                );
                return;
            }
            if (latest.length === 0) {
                event.preventDefault();
                render();
                return;
            }
            draftCheckoutId = [...crypto.getRandomValues(new Uint8Array(16))]
                .map((value) => value.toString(16).padStart(2, "0"))
                .join("");
            form.querySelector("[data-store-checkout-id]").value =
                draftCheckoutId;
            state = captureCheckout(
                { ...latestState, items: latest },
                draftCheckoutId,
                products,
            );
            saveState(state);
            saveDraft();
            form.dataset.submitting = "true";
            const pay = form.querySelector("[data-store-pay]");
            pay.disabled = true;
            pay.setAttribute("aria-busy", "true");
            notice("Ons maak Paystack oop…");
        });
    }
    listen(window, "storage", (event) => {
        if (event.key === CART_KEY || event.key === null) {
            if (event.newValue === null) {
                try {
                    sessionStorage.removeItem(CART_KEY);
                } catch {
                    /* Optional storage. */
                }
            }
            state = readState();
            state = replaceCartItems(
                state,
                normalizeCart(state.items, products),
            );
            render();
            notice("Jou mandjie is in 'n ander oortjie opgedateer.");
        }
    });
    render();
    if (originalItems !== JSON.stringify(state.items) && !completedReference)
        notice(
            "Jou mandjie is opgedateer volgens die produkte wat tans beskikbaar is. Gaan dit asseblief na.",
        );
}

if (typeof document !== "undefined") {
    bindEnhancedNavigation();
    initialize();
    // Native links and Blazor enhanced navigation both need to restore the basket.
    document.addEventListener("enhancedload", initialize);
    document.addEventListener("load", bindEnhancedNavigation, true);
    window.addEventListener("pageshow", () => {
        currentRoot = null;
        initialize();
    });
    window.addEventListener("storage", () => {
        if (!document.querySelector("[data-store]"))
            updateBadges(readState().items);
    });
    new MutationObserver(() => {
        bindEnhancedNavigation();
        const root = document.querySelector("[data-store]");
        if (root !== currentRoot || viewKey(root) !== currentView) initialize();
    }).observe(document.body, { childList: true, subtree: true });
}
