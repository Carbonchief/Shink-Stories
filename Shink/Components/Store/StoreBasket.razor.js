export const CART_KEY = "schink.store.cart.v1";
const COOKIE_KEY = "shink.store.cart";
export const MAX_PER_PRODUCT = 10;
export const MAX_ITEMS = 20;

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

export function readState() {
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

export function saveState(state) {
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

export function updateBadges(items) {
    const count = items.reduce(
        (sum, item) =>
            sum +
            (Number.isInteger(item?.quantity)
                ? Math.max(0, Math.min(MAX_PER_PRODUCT, item.quantity))
                : 0),
        0,
    );
    for (const badge of document.querySelectorAll("[data-store-count]")) {
        badge.textContent = String(Math.min(MAX_ITEMS, count));
    }
    document.documentElement.classList.toggle("has-store-cart", count > 0);
    document.querySelectorAll("[data-store-cart-link]").forEach((link) =>
        link.setAttribute(
            "aria-label",
            `Mandjie met ${count} ${count === 1 ? "item" : "items"}`,
        ),
    );
}
