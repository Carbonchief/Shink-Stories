function isObject(value) {
    return value !== null && typeof value === "object";
}

function getNavigatorUserAgent() {
    return typeof navigator === "object" && typeof navigator.userAgent === "string"
        ? navigator.userAgent
        : "";
}

function isSafariBrowser(userAgent) {
    if (typeof userAgent !== "string" || userAgent.trim() === "") {
        return false;
    }

    const normalizedUserAgent = userAgent.toLowerCase();
    return normalizedUserAgent.includes("safari")
        && !normalizedUserAgent.includes("chrome")
        && !normalizedUserAgent.includes("chromium")
        && !normalizedUserAgent.includes("crios")
        && !normalizedUserAgent.includes("edg")
        && !normalizedUserAgent.includes("opr")
        && !normalizedUserAgent.includes("android");
}

export function getSafariVersion(userAgent = getNavigatorUserAgent()) {
    if (!isSafariBrowser(userAgent)) {
        return null;
    }

    const match = /version\/(\d+)(?:\.\d+)?/i.exec(userAgent);
    if (!match) {
        return null;
    }

    const majorVersion = Number.parseInt(match[1], 10);
    return Number.isFinite(majorVersion) ? majorVersion : null;
}

export function isOldSafari(userAgent = getNavigatorUserAgent()) {
    const safariVersion = getSafariVersion(userAgent);
    return safariVersion !== null && safariVersion < 15;
}

export function lacksModernCrypto(globalWindow = typeof window === "object" ? window : undefined) {
    return !globalWindow
        || !globalWindow.crypto
        || typeof globalWindow.crypto.getRandomValues !== "function"
        || !globalWindow.crypto.subtle;
}

function getSupabaseAuthFlowType() {
    const useImplicitFlow = isOldSafari() || lacksModernCrypto();
    return useImplicitFlow ? "implicit" : "pkce";
}

export function buildGoogleSignInUrl(signInUrl) {
    if (typeof signInUrl !== "string" || signInUrl.trim() === "") {
        return signInUrl;
    }

    const resolvedUrl = new URL(signInUrl, window.location.origin);
    resolvedUrl.searchParams.set("flowType", getSupabaseAuthFlowType());
    return resolvedUrl.toString();
}

export function configureGoogleSignInLink(linkElement, signInUrl) {
    if (!linkElement || typeof linkElement.setAttribute !== "function") {
        return null;
    }

    const resolvedUrl = buildGoogleSignInUrl(signInUrl);
    if (typeof resolvedUrl !== "string" || resolvedUrl.trim() === "") {
        return null;
    }

    linkElement.setAttribute("href", resolvedUrl);
    return resolvedUrl;
}

export function redirectTo(url) {
    if (typeof url !== "string" || url.trim() === "") {
        return false;
    }

    window.location.assign(url);
    return true;
}

async function readJsonPayload(response) {
    try {
        const payload = await response.json();
        if (isObject(payload)) {
            const message = typeof payload.message === "string" && payload.message.trim() !== ""
                ? payload.message
                : null;
            const redirectPath = typeof payload.redirectPath === "string" && payload.redirectPath.trim() !== ""
                ? payload.redirectPath
                : null;

            return { message, redirectPath };
        }
    } catch {
        // Ignore non-JSON responses.
    }

    return { message: null, redirectPath: null };
}

export async function postAuthJson(endpoint, body) {
    try {
        const request = {
            method: "POST",
            credentials: "same-origin"
        };

        if (body !== undefined) {
            request.headers = {
                "Content-Type": "application/json"
            };
            request.body = JSON.stringify(body);
        }

        const response = await fetch(endpoint, request);
        const payload = await readJsonPayload(response);

        return {
            ok: response.ok,
            status: response.status,
            message: payload.message,
            redirectPath: payload.redirectPath
        };
    } catch {
        return {
            ok: false,
            status: 0,
            message: null,
            redirectPath: null
        };
    }
}
