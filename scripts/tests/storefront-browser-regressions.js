// Invoked by test_storefront_browser.py through Playwright CLI's run-code command.
async (page) => {
    const initial = new URL(page.url());
    const base = initial.origin;
    const fixture = initial.searchParams.get("__fixture");
    if (initial.hostname !== "127.0.0.1" || new URL(fixture).hostname !== "127.0.0.1") {
        throw new Error("Store regression tests require an isolated loopback fixture.");
    }
    const results = [];
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const basketCount = async (tab) => Number(await tab.locator("[data-store-count]").first().textContent());
    async function check(name, run) {
        const context = await page.context().browser().newContext();
        const tab = await context.newPage();
        try {
            await tab.goto(base + "/winkel");
            await run(tab, context);
            results.push({ name, passed: true });
        } catch (error) {
            results.push({ name, passed: false, error: error.message });
        } finally {
            await context.close();
        }
    }
    async function add(tab, slug, quantity = 1) {
        for (let index = 0; index < quantity; index++) await tab.locator(`[data-store-add="${slug}"]`).click();
    }
    async function checkout(tab) {
        await tab.locator(".winkel-navigation [data-store-cart-link]").click();
        await tab.getByRole("link", { name: "Gaan na betaalpunt" }).click();
        await tab.locator("#winkel-order-form").waitFor({ state: "visible" });
    }
    async function details(tab, name = "Regressietoets") {
        for (const [field, value] of Object.entries({ naam: name, epos: "review@example.invalid", selfoon: "0821234567",
            adresLyn1: "PUDO Toetslocker", stad: "Pretoria", poskode: "0002" })) {
            await tab.locator(`input[name="${field}"]`).fill(value);
        }
        await tab.locator("input[name=terme]").check();
    }
    async function submit(tab) {
        const before = await (await tab.request.get(fixture + "/_test/orders")).json();
        await tab.locator("[data-store-pay]").click();
        await tab.waitForURL("**/winkel/afreken?**");
        await tab.locator("#winkel-order-form").waitFor({ state: "visible" });
        const orders = await (await tab.request.get(fixture + "/_test/orders")).json();
        assert(orders.length === before.length + 1, "Native checkout did not create exactly one fixture order.");
        return orders[orders.length - 1].reference;
    }
    async function pay(tab, reference) {
        await tab.request.post(fixture + "/_test/paid", { data: { reference } });
        await tab.goto(`${base}/winkel?betaling=sukses&verwysing=${reference}`);
        await tab.locator(".winkel-receipt").waitFor({ state: "visible" });
    }

    await check("checkout details survive basket editing and reload", async (tab) => {
        await add(tab, "tiekie");
        await checkout(tab);
        await details(tab, "Bewaar my besonderhede");
        await tab.getByRole("link", { name: "Terug na jou mandjie" }).click();
        await tab.locator("[data-store-increment=tiekie]").click();
        await tab.getByRole("link", { name: "Gaan na betaalpunt" }).click();
        assert(await tab.locator("input[name=naam]").inputValue() === "Bewaar my besonderhede", "Editing the basket lost the entered name.");
        assert(await tab.locator("input[name=adresLyn1]").inputValue() === "PUDO Toetslocker", "Editing the basket lost the address.");
        await tab.locator("input[name=adresLyn1]").fill("PUDO Nuwe locker");
        await tab.reload();
        assert(await tab.locator("input[name=adresLyn1]").inputValue() === "PUDO Nuwe locker", "Reload reverted the corrected address.");
        const basket = await tab.evaluate(() => localStorage.getItem("schink.store.cart.v1"));
        assert(!basket.includes("review@example.invalid") && !basket.includes("PUDO"), "Personal details leaked into persistent basket storage.");
    });
    await check("overlapping checkouts retain both protected receipts", async (tab) => {
        await add(tab, "tiekie");
        await checkout(tab);
        await details(tab);
        const first = await submit(tab);
        await tab.locator("input[name=terme]").check();
        const second = await submit(tab);
        await pay(tab, first);
        assert(await tab.evaluate(() => sessionStorage.getItem("schink.store.checkout.v1")) !== null,
            "Completing the first order erased the second checkout's draft.");
        await pay(tab, second);
        assert(await basketCount(tab) === 0, "Verified purchases did not clear the checked-out basket.");
        assert(await tab.evaluate(() => sessionStorage.getItem("schink.store.checkout.v1")) === null,
            "The completed checkout's personal draft was not cleared.");
    });
    await check("payment return preserves a cleared and rebuilt basket", async (tab) => {
        await add(tab, "tiekie", 2);
        await checkout(tab);
        await details(tab);
        const reference = await submit(tab);
        await tab.locator(".winkel-navigation [data-store-cart-link]").click();
        await tab.locator("[data-store-clear]").click();
        await tab.locator("[data-store-empty]").getByRole("link", { name: "Gaan winkel toe" }).click();
        await add(tab, "tiekie");
        await pay(tab, reference);
        assert(await basketCount(tab) === 1, "Payment removed an item from the new basket.");
        await tab.reload();
        assert(await basketCount(tab) === 1, "Refreshing the receipt removed a later basket item.");
    });
    await check("later quantity additions survive payment and receipt refresh", async (tab) => {
        await add(tab, "tiekie", 2);
        await checkout(tab);
        await details(tab);
        const reference = await submit(tab);
        await tab.locator(".winkel-home-link").click();
        await add(tab, "tiekie");
        await pay(tab, reference);
        assert(await basketCount(tab) === 1, "The completed checkout consumed a later quantity addition.");
        await tab.reload();
        assert(await basketCount(tab) === 1, "Refreshing the receipt consumed the later addition.");
    });
    await check("removing and re-adding one product preserves only the new selection", async (tab) => {
        await add(tab, "tiekie", 2);
        await add(tab, "georgie");
        await checkout(tab);
        await details(tab);
        const reference = await submit(tab);
        await tab.locator(".winkel-navigation [data-store-cart-link]").click();
        await tab.locator("[data-store-remove=tiekie]").click();
        await tab.locator(".winkel-home-link").click();
        await add(tab, "tiekie");
        await pay(tab, reference);
        assert(await basketCount(tab) === 1, "Payment did not preserve the re-added product.");
        await tab.locator(".winkel-navigation [data-store-cart-link]").click();
        assert(await tab.locator("[data-store-quantity=tiekie]").inputValue() === "1", "The re-added Tiekie was removed.");
        assert(await tab.locator("[data-store-cart-item=georgie]").isHidden(), "The original purchased Georgie was not removed.");
    });
    await check("a second tab can rebuild the basket without losing it to a payment return", async (tab, context) => {
        await add(tab, "tiekie", 2);
        await checkout(tab);
        await details(tab);
        const reference = await submit(tab);
        const other = await context.newPage();
        await other.goto(base + "/winkel/mandjie");
        await other.locator("[data-store-clear]").click();
        await other.locator("[data-store-empty]").getByRole("link", { name: "Gaan winkel toe" }).click();
        await add(other, "tiekie");
        await pay(tab, reference);
        assert(await basketCount(tab) === 1, "Payment consumed another tab's rebuilt basket.");
        await other.waitForFunction(() => document.querySelector("[data-store-count]").textContent === "1");
    });
    await check("corrected delivery details survive a failed checkout and further basket edits", async (tab) => {
        await add(tab, "tiekie");
        await checkout(tab);
        await details(tab);
        await submit(tab);
        await tab.locator("input[name=adresLyn1]").fill("PUDO Gekorrigeerde locker");
        await tab.getByRole("link", { name: "Terug na jou mandjie" }).click();
        await tab.locator("[data-store-increment=tiekie]").click();
        await tab.getByRole("link", { name: "Gaan na betaalpunt" }).click();
        assert(await tab.locator("input[name=adresLyn1]").inputValue() === "PUDO Gekorrigeerde locker",
            "Basket editing restored an older submitted address instead of the correction.");
        await tab.reload();
        assert(!await tab.locator("input[name=terme]").isChecked(), "Terms consent was silently restored.");
    });
    await check("store navigation, responsive layouts and checkout guards remain intact", async (tab) => {
        await tab.locator(".winkel-navigation [data-store-cart-link]").click();
        await tab.locator("[data-store-empty]").waitFor({ state: "visible" });
        await tab.locator("[data-store-empty]").getByRole("link", { name: "Gaan winkel toe" }).click();
        await tab.locator("[data-store-search]").fill("tiekie");
        assert(await tab.locator("[data-store-card]:visible").count() === 1, "Product search failed.");
        await tab.locator("[data-store-search]").fill("no-matching-product");
        await tab.locator("[data-store-no-results]").waitFor({ state: "visible" });
        await tab.locator("[data-store-reset-filters]").click();
        await tab.locator("[data-store-sort]").selectOption("name");
        assert(await tab.locator("[data-store-card]").first().getAttribute("data-slug") === "georgie", "Product sorting failed.");
        for (const width of [320, 390, 768, 1440]) {
            await tab.setViewportSize({ width, height: 900 });
            assert(!await tab.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `Catalogue overflows at ${width}px.`);
        }
        await add(tab, "tiekie");
        await add(tab, "georgie");
        await checkout(tab);
        await details(tab);
        assert(await tab.locator("[data-store-post-total]").inputValue() === "58000", "Mixed basket did not include exactly one R80 delivery fee.");
        const fields = await tab.locator("#winkel-order-form").evaluate(form => Object.fromEntries(new FormData(form)));
        const noToken = { ...fields };
        delete noToken.__RequestVerificationToken;
        const cases = [
            [noToken, "verval"], [{ ...fields, cartTotalCents: "1" }, "verander"],
            [{ ...fields, "qty-removed": "1" }, "nie meer beskikbaar"],
            [{ ...fields, "qty-tiekie": "11" }, "geldige hoeveelheid"],
            [{ ...fields, terme: "" }, "Aanvaar"]
        ];
        for (const [form, message] of cases) {
            const response = await tab.request.post(base + "/winkel/koop/paystack", { form, maxRedirects: 0 });
            assert(response.status() === 302 && decodeURIComponent(response.headers().location).includes(message),
                `Checkout did not reject invalid input: ${message}`);
        }
        for (const width of [320, 390, 768, 1440]) {
            await tab.setViewportSize({ width, height: 900 });
            assert(!await tab.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `Checkout overflows at ${width}px.`);
        }
        await tab.goto(base + "/winkel?betaling=sukses&verwysing=W-FORGED");
        assert(await tab.locator(".winkel-receipt").count() === 0, "A query parameter forged a paid receipt.");
        assert(await basketCount(tab) === 2, "A forged payment URL cleared the basket.");
    });
    const summary = { passed: results.filter(result => result.passed).length, failed: results.filter(result => !result.passed).length, results };
    if (summary.failed) throw new Error(JSON.stringify(summary));
    return summary;
}
