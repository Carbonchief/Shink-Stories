function isObject(value) {
    return value !== null && typeof value === "object";
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
