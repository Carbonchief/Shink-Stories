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

export function readInputValues(inputIds) {
    const values = {};
    if (!Array.isArray(inputIds)) {
        return values;
    }

    for (const inputId of inputIds) {
        if (typeof inputId !== "string" || inputId.trim() === "") {
            continue;
        }

        const element = document.getElementById(inputId);
        if (element instanceof HTMLInputElement ||
            element instanceof HTMLTextAreaElement ||
            element instanceof HTMLSelectElement) {
            values[inputId] = element.value ?? "";
        }
    }

    return values;
}

const SUPABASE_RECOVERY_PARAM_NAMES = [
    "access_token",
    "refresh_token",
    "expires_in",
    "expires_at",
    "token_type",
    "type",
    "error",
    "error_code",
    "error_description",
    "provider_token",
    "provider_refresh_token"
];

function readNamedParam(paramName, ...searchParams) {
    for (const params of searchParams) {
        if (!(params instanceof URLSearchParams)) {
            continue;
        }

        const value = params.get(paramName);
        if (typeof value === "string" && value.trim() !== "") {
            return value;
        }
    }

    return null;
}

export function readSupabaseRecoveryState() {
    if (typeof window !== "object" || typeof window.location !== "object") {
        return {
            accessToken: null,
            refreshToken: null,
            type: null,
            errorCode: null,
            errorDescription: null
        };
    }

    const currentUrl = new URL(window.location.href);
    const queryParams = currentUrl.searchParams;
    const hashText = currentUrl.hash.startsWith("#")
        ? currentUrl.hash.slice(1)
        : currentUrl.hash;
    const hashParams = new URLSearchParams(hashText);

    const state = {
        accessToken: readNamedParam("access_token", hashParams, queryParams),
        refreshToken: readNamedParam("refresh_token", hashParams, queryParams),
        type: readNamedParam("type", hashParams, queryParams),
        errorCode: readNamedParam("error_code", hashParams, queryParams),
        errorDescription: readNamedParam("error_description", hashParams, queryParams)
    };

    const hasSensitiveParams = SUPABASE_RECOVERY_PARAM_NAMES.some((paramName) =>
        queryParams.has(paramName) || hashParams.has(paramName));

    if (hasSensitiveParams && typeof window.history?.replaceState === "function") {
        const cleanUrl = new URL(currentUrl.toString());
        for (const paramName of SUPABASE_RECOVERY_PARAM_NAMES) {
            cleanUrl.searchParams.delete(paramName);
        }

        cleanUrl.hash = "";
        window.history.replaceState({}, document.title, cleanUrl.toString());
    }

    return state;
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
