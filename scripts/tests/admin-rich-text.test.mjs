import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../../Shink/wwwroot/js/admin-rich-text.js", import.meta.url));
const { bufferEditorChanges } = await import(`data:text/javascript;base64,${source.toString("base64")}`);

function pendingDelivery() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

test("typing marks the draft dirty immediately and exports once on save", async () => {
    let reads = 0, dirty = 0;
    const deliveries = [];
    const buffer = bufferEditorChanges(() => { reads++; return "last keystroke"; },
        async html => deliveries.push(html), () => dirty++);
    for (let i = 0; i < 12; i++) buffer.changed();
    assert.equal(dirty, 1);
    assert.equal(reads, 0);
    await buffer.flush();
    await buffer.flush();
    assert.equal(reads, 1);
    assert.deepEqual(deliveries, ["last keystroke"]);
});

test("saving while a preview is in flight waits for the final edit", async () => {
    const first = pendingDelivery();
    const last = pendingDelivery();
    let html = "first";
    const calls = [];
    const buffer = bufferEditorChanges(() => html, value => {
        calls.push(value);
        return value === "first" ? first.promise : last.promise;
    });

    buffer.changed();
    const preview = buffer.flush();
    html = "second";
    buffer.changed();
    let saved = false;
    const save = Promise.all([buffer.flush(), buffer.flush()]).then(() => { saved = true; });
    first.resolve();
    await preview;
    assert.deepEqual(calls, ["first", "second"]);
    assert.equal(saved, false);
    last.resolve();
    await save;
});

test("a failed delivery retries the latest edit and a discarded edit never exports", async () => {
    let failed = false, html = "first";
    const delivered = [];
    const buffer = bufferEditorChanges(() => html, async value => {
        if (!failed) {
            failed = true;
            throw new Error("disconnected");
        }
        delivered.push(value);
    });
    buffer.changed();
    await assert.rejects(buffer.flush());
    html = "latest";
    buffer.changed();
    await buffer.flush();
    buffer.changed();
    buffer.cancel();
    await buffer.flush();
    assert.deepEqual(delivered, ["latest"]);
});
