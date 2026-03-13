function isObject(value) {
    return value !== null && typeof value === "object";
}

async function readJsonMessage(response) {
    try {
        const payload = await response.json();
        if (isObject(payload) && typeof payload.message === "string" && payload.message.trim() !== "") {
            return payload.message;
        }
    } catch {
        // Ignore non-JSON responses.
    }

    return null;
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
        const message = await readJsonMessage(response);

        return {
            ok: response.ok,
            status: response.status,
            message
        };
    } catch {
        return {
            ok: false,
            status: 0,
            message: null
        };
    }
}
