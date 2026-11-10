// Invoked by storefront_browser_regressions.py through Playwright CLI's run-code command.
async (page) => {
    const initial = new URL(page.url());
    const base = initial.origin;
    const fixture = initial.searchParams.get("__fixture");
    const selectedSuite = initial.searchParams.get("__suite");
    let currentSuite = "core";
    if (initial.hostname !== "127.0.0.1" || new URL(fixture).hostname !== "127.0.0.1") {
        throw new Error("Store regression tests require an isolated loopback fixture.");
    }
    const results = [];
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const basketCount = async (tab) => Number(await tab.locator("[data-store-count]").first().textContent());
    async function check(name, run) {
        if (selectedSuite && selectedSuite !== currentSuite) return;
        const context = await page.context().browser().newContext();
        const tab = await context.newPage();
        tab.setDefaultTimeout(10000);
        tab.setDefaultNavigationTimeout(15000);
        try {
            await tab.goto(base + "/winkel");
            await run(tab, context);
            results.push({ name, passed: true });
        } catch (error) {
            await tab.screenshot({ path: `failure-${results.length}.png`, animations: "disabled" });
            results.push({ name, passed: false, error: error.message, url: tab.url() });
        } finally {
            await context.close();
        }
    }
    async function add(tab, slug, quantity = 1) {
        await tab.locator(`[data-store-open-product="${slug}"]`).click();
        await tab.locator("[data-store-quick-quantity]").fill(String(quantity));
        await tab.locator("[data-store-quick-add]").click();
        await tab.locator("[data-store-quick-view]").waitFor({ state: "hidden" });
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
        const [response] = await Promise.all([
            tab.waitForResponse(response => response.url().includes("/winkel/koop/paystack") && response.request().method() === "POST"),
            tab.locator("[data-store-pay]").click()
        ]);
        assert(response.status() < 400, `Checkout returned HTTP ${response.status()}.`);
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
        await tab.locator("input[data-store-sort][value=name]").check();
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
    await check("store filters work in the sidebar and on smaller screens", async (tab) => {
        assert(!await tab.locator(".winkel-page > noscript").isVisible(), "The JavaScript fallback is visible while the store is running.");
        const toggle = tab.locator("[data-store-filter-toggle]");
        const panel = tab.locator("[data-store-filter-panel]");
        assert(await panel.isVisible(), "Desktop filters are not visible beside the products.");
        assert(await tab.locator("[data-store-grid]").evaluate(grid => getComputedStyle(grid).gridTemplateColumns.split(" ").length) === 3,
            "The desktop catalogue does not use three product columns.");
        await tab.locator("input[data-store-sort][value=name-desc]").check();
        assert(await tab.locator("[data-store-card]").first().getAttribute("data-slug") === "tiekie", "Descending name sorting failed.");
        await toggle.click();
        assert(await panel.isHidden() && await toggle.getAttribute("aria-expanded") === "false", "The desktop filter toggle did not hide the sidebar.");
        await toggle.click();
        assert(await panel.isVisible(), "The desktop filter toggle did not reopen the sidebar.");
        await tab.setViewportSize({ width: 390, height: 844 });
        await panel.waitFor({ state: "hidden" });
        await toggle.click();
        await panel.waitFor({ state: "visible" });
        await tab.locator("[data-store-free-filter]").check();
        await tab.locator("[data-store-no-results]").waitFor({ state: "visible" });
        await tab.locator("[data-store-reset-filters]").click();
        assert(await tab.locator("[data-store-card]:visible").count() === 4, "Reset did not restore the available products.");
        assert(await tab.locator("input[data-store-sort][value=featured]").isChecked(), "Reset did not restore the default sort order.");
        assert(!await tab.locator("[data-store-free-filter]").isChecked(), "Reset did not clear the delivery filter.");
        assert(!await tab.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), "Expanded mobile filters overflow horizontally.");
        await toggle.click();
        await add(tab, "tiekie");
        assert(await basketCount(tab) === 1, "The image's add button did not update the basket.");
    });
    await check("product modal and floating cart preserve quantities and enforce basket limits", async (tab) => {
        const modal = tab.locator("[data-store-quick-view]");
        const quantity = tab.locator("[data-store-quick-quantity]");
        const floating = tab.locator(".winkel-floating-cart");
        const openTiekie = tab.locator("[data-store-open-product=tiekie]");
        await openTiekie.click();
        assert(await modal.isVisible() && await basketCount(tab) === 0, "Opening the product modal changed the basket.");
        assert(await tab.locator("[data-store-quick-name]").textContent() === "Tiekie", "The modal showed the wrong product.");
        await tab.keyboard.press("Escape");
        await modal.waitFor({ state: "hidden" });
        assert(await openTiekie.evaluate(button => document.activeElement === button), "Closing the modal did not return keyboard focus.");
        for (const width of [320, 390, 768, 1440]) {
            await tab.setViewportSize({ width, height: 900 });
            await openTiekie.click();
            assert(await modal.evaluate(dialog => {
                const bounds = dialog.getBoundingClientRect();
                return bounds.left >= 0 && bounds.right <= innerWidth && bounds.top >= 0 && bounds.bottom <= innerHeight && dialog.scrollWidth <= dialog.clientWidth + 1;
            }), `The product modal overflows at ${width}px.`);
            if (width === 390 || width === 1440) await tab.screenshot({ path: `product-modal-${width}.png`, animations: "disabled" });
            await tab.locator("[data-store-quick-close]").click();
        }
        await openTiekie.click();
        await quantity.fill("0");
        await tab.locator("[data-store-quick-add]").click();
        assert(await modal.isVisible() && await basketCount(tab) === 0 && await tab.locator("[data-store-quick-error]").isVisible(), "Invalid modal quantity mutated the basket.");
        await quantity.fill("9");
        await tab.locator("[data-store-quick-add]").click();
        await modal.waitFor({ state: "hidden" });
        assert(await floating.getAttribute("aria-label") === "Mandjie met 9 items", "The floating cart did not update its accessible count.");
        await tab.screenshot({ path: "floating-cart-desktop.png", animations: "disabled" });
        await openTiekie.click();
        assert(await quantity.inputValue() === "1", "Reopening the modal kept an old quantity.");
        await quantity.fill("2");
        await tab.locator("[data-store-quick-buy]").click();
        assert(await modal.isVisible() && await basketCount(tab) === 9, "Buy now bypassed the per-product limit.");
        await quantity.fill("1");
        await tab.locator("[data-store-quick-add]").click();
        await modal.waitFor({ state: "hidden" });
        await tab.locator("[data-store-open-product=georgie]").click();
        assert(await tab.locator("[data-store-quick-name]").textContent() === "Georgie", "Switching products left stale modal content.");
        await quantity.fill("10");
        await tab.locator("[data-store-quick-buy]").click();
        await tab.waitForURL("**/winkel/afreken");
        assert(await basketCount(tab) === 20, "Buy now discarded the existing basket or ignored the selected quantity.");
        await tab.locator(".winkel-home-link").click();
        await tab.locator("[data-store-open-product=suurlemoentjie]").click();
        await tab.locator("[data-store-quick-buy]").click();
        assert(await modal.isVisible() && await basketCount(tab) === 20, "The modal bypassed the total basket limit.");
        await tab.mouse.click(5, 5);
        await modal.waitFor({ state: "hidden" });
        await tab.setViewportSize({ width: 390, height: 844 });
        await tab.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
        assert(await floating.evaluate(link => {
            const bounds = link.getBoundingClientRect();
            return getComputedStyle(link).position === "fixed" && bounds.right < innerWidth && bounds.bottom < innerHeight && bounds.width >= 44;
        }), "The floating cart did not remain accessible at the bottom right after scrolling.");
        await floating.click();
        await tab.waitForURL("**/winkel/mandjie");
        await tab.locator("[data-store-clear]").click();
        assert(await tab.locator(".winkel-floating-cart [data-store-count]").textContent() === "0", "Clearing the basket left a stale floating cart count.");
    });
    currentSuite = "account";
    await check("checkout sign-in prefills account details and remembers delivery only when selected", async (tab, context) => {
        await add(tab, "tiekie");
        await checkout(tab);
        await tab.locator("input[name=naam]").fill("Keep my guest name");
        await tab.locator(".winkel-checkout-signin a").click();
        await tab.waitForURL("**/teken-in?returnUrl=**");
        // The sign-in form uses InteractiveServer; wait for its visible link setup before submitting.
        await tab.waitForFunction(() => document.querySelector(".login-submit-google")?.getAttribute("href")?.includes("flowType="));
        await tab.locator("#signin-email").fill("buyer@example.invalid");
        await tab.locator("#signin-password").fill("Local-checkout-test-123!");
        await tab.locator("button.login-submit[type=submit]").click();
        await tab.waitForURL("**/winkel/afreken");
        assert(await tab.locator("input[name=naam]").inputValue() === "Keep my guest name", "Signing in overwrote a guest edit.");
        assert(await tab.locator("input[name=epos]").inputValue() === "buyer@example.invalid", "Blank guest email did not prefill from the account.");
        assert(await tab.locator("input[name=selfoon]").inputValue() === "0831234567", "The profile phone was not prefilled.");
        assert(await tab.locator("[data-store-save-details]").isChecked(), "Remember details was not checked by default.");
        assert(!await tab.locator("input[name=terme]").isChecked(), "Prefill accepted the terms automatically.");
        await details(tab, "Saved Buyer");
        await tab.locator("input[name=adresLyn2]").fill("Locker entrance");
        await tab.locator("textarea[name=notas]").fill("This order only");
        await tab.locator("[data-store-save-details]").uncheck();
        await submit(tab);
        assert(!await tab.locator("[data-store-save-details]").isChecked(), "Failed payment silently re-enabled saving.");
        let preferences = await (await tab.request.get(fixture + "/_test/preferences")).json();
        assert(!preferences["buyer@example.invalid"], "An unchecked save option still persisted personal details.");
        await tab.locator("[data-store-save-details]").check();
        await tab.locator("input[name=terme]").check();
        await submit(tab);
        preferences = await (await tab.request.get(fixture + "/_test/preferences")).json();
        assert(preferences["buyer@example.invalid"]?.delivery_address_line_1 === "PUDO Toetslocker", "Delivery details were not saved against the signed-in account.");
        assert(!("notes" in preferences["buyer@example.invalid"]), "Order-specific notes were saved as account preferences.");
        // A new tab starts a future checkout without the current order's session draft.
        tab = await context.newPage();
        await tab.goto(base + "/winkel/afreken");
        assert(await tab.locator("input[name=naam]").inputValue() === "Saved Buyer", "A later checkout did not load the saved name.");
        assert(await tab.locator("input[name=epos]").inputValue() === "review@example.invalid", "A saved contact email was confused with the account owner.");
        assert(await tab.locator("input[name=adresLyn1]").inputValue() === "PUDO Toetslocker", "A later checkout did not load the saved delivery address.");
        assert(await tab.locator("input[name=adresLyn2]").inputValue() === "Locker entrance", "The optional delivery address was lost.");
        assert(await tab.locator("textarea[name=notas]").inputValue() === "", "Order notes leaked into a future purchase.");
        await tab.setViewportSize({ width: 1440, height: 1000 });
        await tab.reload();
        await tab.evaluate(() => document.fonts.ready);
        await tab.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
        await tab.screenshot({ path: "checkout-account-desktop.png", animations: "disabled" });
        await tab.setViewportSize({ width: 390, height: 844 });
        await tab.reload();
        await tab.evaluate(() => document.fonts.ready);
        await tab.locator("[data-store-save-details]").scrollIntoViewIfNeeded();
        await tab.screenshot({ path: "checkout-account-mobile.png", animations: "disabled" });
        await tab.locator("input[name=naam]").fill("");
        await tab.locator("[data-store-save-details]").uncheck();
        await tab.reload();
        assert(await tab.locator("input[name=naam]").inputValue() === "", "Account prefill overwrote an intentionally cleared field.");
        assert(!await tab.locator("[data-store-save-details]").isChecked(), "Reload overwrote the shopper's save preference.");
        for (const width of [320, 390, 768, 1440]) {
            await tab.setViewportSize({ width, height: 900 });
            assert(!await tab.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `Signed-in checkout overflows at ${width}px.`);
        }
    });
    await check("changing accounts or signing out cannot restore another customer's checkout draft", async (tab, context) => {
        await add(tab, "georgie");
        const login = async (email) => {
            const response = await tab.request.post(base + "/api/auth/login", { data: { email, password: "Local-checkout-test-123!" } });
            assert(response.ok(), "Local fixture sign-in failed: " + await response.text());
        };
        await login("buyer@example.invalid");
        await tab.goto(base + "/winkel/afreken");
        await tab.locator("input[name=adresLyn1]").fill("Private draft for Buyer");
        await login("other@example.invalid");
        await tab.reload();
        assert(await tab.locator("input[name=naam]").inputValue() === "Other Account", "Switching accounts restored another shopper's name.");
        assert(await tab.locator("input[name=epos]").inputValue() === "other@example.invalid", "Switching accounts restored another shopper's email.");
        assert(await tab.locator("input[name=adresLyn1]").inputValue() === "", "Switching accounts leaked a saved address or old draft.");
        await tab.locator("input[name=adresLyn1]").fill("Private draft for Other");
        await context.clearCookies();
        await tab.reload();
        assert(await tab.locator(".winkel-checkout-signin").isVisible(), "Signed-out checkout did not offer sign-in.");
        assert(await tab.locator("input[name=naam]").inputValue() === "" && await tab.locator("input[name=adresLyn1]").inputValue() === "", "Signing out leaked the prior account's draft.");
    });
    await check("modal focus, scroll lock and handlers recover after keyboard and browser navigation", async (tab) => {
        const errors = [];
        tab.on("pageerror", error => errors.push(error.message));
        await tab.locator("[data-store-open-product=tiekie]").click();
        for (let index = 0; index < 10; index++) {
            await tab.keyboard.press("Tab");
            assert(await tab.locator("[data-store-quick-view]").evaluate(dialog => !document.hasFocus() || dialog.contains(document.activeElement)), "Keyboard focus reached background controls while the modal was open.");
        }
        await tab.keyboard.press("Escape");
        await tab.waitForFunction(() => !document.body.classList.contains("winkel-quick-view-open"));
        assert(!await tab.locator("body").evaluate(body => body.classList.contains("winkel-quick-view-open")), "Escape left page scrolling locked.");
        await tab.locator("[data-store-open-product=tiekie]").click();
        await tab.goto(base + "/winkel/mandjie");
        await tab.goBack();
        await tab.locator("[data-store-open-product=tiekie]").waitFor({ state: "visible" });
        assert(!await tab.locator("body").evaluate(body => body.classList.contains("winkel-quick-view-open")), "Browser back left page scrolling locked.");
        await add(tab, "tiekie");
        assert(await basketCount(tab) === 1, "Browser navigation duplicated or lost the modal's add handler.");
        assert(errors.length === 0, "Browser script errors: " + errors.join(", "));
    });
    await check("main-site cart appears only for a non-empty basket, independently of login", async (tab, context) => {
        await tab.goto(base + "/");
        const headerCart = tab.locator(".winkel-header-cart");
        assert(await headerCart.isHidden(), "An empty cart appeared on the main site for a guest.");
        const login = await tab.request.post(base + "/api/auth/login", { data: {
            email: "buyer@example.invalid", password: "Local-checkout-test-123!"
        } });
        assert(login.ok(), "Local fixture sign-in failed.");
        await tab.reload();
        await tab.locator(".site-header .nav-controls").waitFor({ state: "visible" });
        assert(await headerCart.isHidden(), "Normal sign-in displayed an empty cart.");
        await tab.goto(base + "/winkel");
        assert(await headerCart.isVisible(), "The store lost its empty cart navigation.");
        await add(tab, "tiekie", 2);
        await tab.goto(base + "/meer-oor-ons");
        await headerCart.waitFor({ state: "visible" });
        assert(await headerCart.locator("[data-store-count]").textContent() === "2", "A saved basket did not appear on the main site with its count.");
        await tab.reload();
        assert(await headerCart.isVisible(), "Reloading lost the saved basket's header link.");
        const other = await context.newPage();
        await other.goto(base + "/winkel/mandjie");
        await other.locator("[data-store-clear]").click();
        await headerCart.waitFor({ state: "hidden" });
        assert(await other.locator(".winkel-header-cart").isVisible(), "Clearing the cart hid the store's own navigation.");
        await other.goto(base + "/winkel");
        await add(other, "georgie");
        await headerCart.waitFor({ state: "visible" });
        assert(await headerCart.locator("[data-store-count]").textContent() === "1", "Adding in another tab did not restore the main-site cart.");
        const noScript = await page.context().browser().newContext({ javaScriptEnabled: false });
        try {
            const initial = await noScript.newPage();
            await initial.goto(base + "/");
            assert(await initial.locator(".winkel-header-cart").isHidden(), "An empty cart flashes before JavaScript initialises.");
        } finally {
            await noScript.close();
        }
    });
    currentSuite = "collections";
    await check("collections filter enabled products and label cards, modals and details", async (tab) => {
        assert(await tab.locator("[data-store-card]").count() === 3, "Disabled products appeared in the public catalogue.");
        for (const [slug, label] of [["kinderboeke", "Kinderboeke"], ["storietjommies", "StorieTjommies"], ["pakkette", "Pakkette"]]) {
            const radio = tab.locator(`[data-store-collection][value="${slug}"]`);
            assert(await radio.locator("..").locator("small").textContent() === "1", "Collection counts include disabled or unrelated products.");
            await radio.check();
            assert(await tab.locator("[data-store-card]:visible").count() === 1, "The collection filter shows unrelated products.");
            assert(await tab.locator("[data-store-card]:visible .winkel-product-type").textContent() === label, "Card label differs from its collection.");
            await tab.locator("[data-store-card]:visible [data-store-open-product]").click();
            assert(await tab.locator("[data-store-quick-collection]").textContent() === label, "Modal collection label is stale.");
            await tab.keyboard.press("Escape");
        }
        await tab.locator("[data-store-search]").fill("Toetsboek");
        assert(await tab.locator("[data-store-no-results]").isVisible(), "Search did not combine with the collection filter.");
        await tab.locator("[data-store-reset-filters]").click();
        assert(await tab.locator("[data-store-card]:visible").count() === 3, "Reset did not restore all products.");
        await tab.locator('[data-store-collection][value="kinderboeke"]').check();
        await tab.locator("[data-store-free-filter]").check();
        assert(await tab.locator("[data-store-no-results]").isVisible(), "Delivery filtering ignored the selected collection.");
        await tab.locator("[data-store-reset-filters]").click();
        await tab.locator('[data-store-sort][value="price-asc"]').check();
        assert(await tab.locator("[data-store-card]:visible").first().getAttribute("data-slug") === "toets-tjommie", "Sorting regressed with collection metadata.");
        for (const width of [320, 390, 1440]) {
            await tab.setViewportSize({ width, height: 900 });
            assert(!await tab.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `Collections overflow at ${width}px.`);
        }
        await tab.goto(base + "/winkel/produk/toets-boek");
        assert(await tab.locator(".winkel-product-detail .winkel-section-kicker").textContent() === "Kinderboeke", "Product details still use a hardcoded collection.");
    });
    await check("admin collection assignments persist and refresh the public catalogue in both languages", async (tab) => {
        const response = await tab.request.post(base + "/api/auth/login", { data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" } });
        assert(response.ok(), "Local admin sign-in failed.");
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Winkel", exact: true }).click();
        await tab.locator(".store-admin-list-item").filter({ hasText: "Toetsboek" }).click();
        const selector = tab.getByLabel("Versameling", { exact: true });
        assert(await selector.inputValue() === "kinderboeke", "Admin did not load the persisted collection.");
        assert(await selector.locator("option:not([disabled])").allTextContents().then(labels => labels.join("|")) === "Kinderboeke|StorieTjommies|Pakkette", "Afrikaans collection choices are incomplete.");
        await selector.selectOption("pakkette");
        await tab.getByRole("button", { name: "Stoor produk", exact: true }).click();
        await tab.locator(".store-admin-section .admin-feedback.is-success").waitFor();
        await tab.evaluate(() => localStorage.setItem("shink.admin.language", "en"));
        await tab.reload();
        await tab.getByRole("tab", { name: "Shop", exact: true }).click();
        await tab.locator(".store-admin-list-item").filter({ hasText: "Toetsboek" }).click();
        const english = tab.getByLabel("Collection", { exact: true });
        assert(await english.inputValue() === "pakkette", "The saved collection did not survive an admin reload.");
        assert(await english.locator("option:not([disabled])").allTextContents().then(labels => labels.join("|")) === "Children's books|Story buddies|Bundles", "English collection choices are incomplete.");
        await tab.screenshot({ path: "admin-product-collection.png", animations: "disabled" });
        await tab.getByRole("button", { name: "New product", exact: true }).click();
        await tab.getByRole("heading", { name: "New product", exact: true }).waitFor();
        assert(await english.inputValue() === "", "A new product silently inherited a collection.");
        await tab.getByRole("button", { name: "Save product", exact: true }).click();
        await tab.getByText("Choose a valid product collection.", { exact: true }).waitFor();
        await tab.setViewportSize({ width: 390, height: 900 });
        assert(await english.evaluate(element => element.getBoundingClientRect().right <= innerWidth), "The admin collection selector overflows on mobile.");
        await tab.goto(base + "/winkel");
        await tab.locator("[data-store-filter-toggle]").click();
        await tab.locator('[data-store-collection][value="pakkette"]').check();
        assert(await tab.locator("[data-store-card]:visible").count() === 2, "The public cache did not refresh after admin reassignment.");
        assert(await tab.locator('[data-store-card][data-slug="toets-boek"] .winkel-product-type').textContent() === "Pakkette", "The reassigned card label is stale.");
        assert(await tab.locator('[data-store-collection][value="kinderboeke"]').locator("..").locator("small").textContent() === "0", "The old collection retained the moved product.");
    });
    for (const language of ["af", "en"]) {
        await check(`admin protects unsaved assignments when switching, refreshing or creating (${language})`, async (tab) => {
            const en = language === "en";
            const copy = en ? {
                tab: "Shop", collection: "Collection", refresh: "Refresh", create: "New product", save: "Save product", cancel: "Cancel",
                warning: "You have unsaved changes. Are you sure you want to discard them?"
            } : {
                tab: "Winkel", collection: "Versameling", refresh: "Herlaai", create: "Nuwe produk", save: "Stoor produk", cancel: "Kanselleer",
                warning: "Jy het ongestoorde veranderinge. Is jy seker jy wil dit weggooi?"
            };
            const login = await tab.request.post(base + "/api/auth/login", { data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" } });
            assert(login.ok(), "Local admin sign-in failed.");
            await tab.evaluate(language => localStorage.setItem("shink.admin.language", language), language);
            await tab.goto(base + "/admin");
            await tab.getByRole("tab", { name: copy.tab, exact: true }).click();
            const book = tab.locator(".store-admin-list-item").filter({ hasText: "Toetsboek" });
            const buddy = tab.locator(".store-admin-list-item").filter({ hasText: "Toetstjommie" });
            await book.click();
            const collection = tab.getByLabel(copy.collection, { exact: true });
            const saved = await collection.inputValue();
            const changed = saved === "kinderboeke" ? "storietjommies" : "kinderboeke";
            let accept = false;
            const warnings = [];
            tab.on("dialog", async dialog => {
                warnings.push(dialog.message());
                if (accept) await dialog.accept(); else await dialog.dismiss();
            });
            const discard = async (action, approved) => {
                accept = approved;
                const before = warnings.length;
                await Promise.all([tab.waitForEvent("dialog", { timeout: 2000 }), action()]);
                assert(warnings.length === before + 1 && warnings.at(-1) === copy.warning, "Missing or incorrectly translated discard confirmation.");
            };
            await collection.selectOption(changed);
            await discard(() => buddy.click(), false);
            assert(await collection.inputValue() === changed, "Declining a product switch lost the assignment.");
            await discard(() => tab.getByRole("button", { name: copy.refresh, exact: true }).click(), false);
            assert(await collection.inputValue() === changed, "Declining Refresh lost the assignment.");
            await discard(() => tab.getByRole("button", { name: copy.create, exact: true }).click(), false);
            assert(await collection.inputValue() === changed, "Declining New product lost the assignment.");
            await book.click();
            assert(await collection.inputValue() === changed && warnings.length === 3, "Selecting the current product reset its edits or prompted unnecessarily.");
            await discard(() => buddy.click(), true);
            await tab.getByRole("heading", { name: "Toetstjommie", exact: true }).waitFor();
            await book.click();
            await tab.getByRole("heading", { name: "Toetsboek", exact: true }).waitFor();
            assert(await collection.inputValue() === saved, "Approved switching did not discard the old edit.");
            await collection.selectOption(changed);
            await discard(() => tab.getByRole("button", { name: copy.refresh, exact: true }).click(), true);
            await tab.waitForFunction(expected => document.querySelector('select[aria-labelledby="store-admin-collection-label"]')?.value === expected, saved);
            await collection.selectOption(changed);
            await discard(() => tab.getByRole("button", { name: copy.create, exact: true }).click(), true);
            await tab.getByRole("heading", { name: copy.create, exact: true }).waitFor();
            assert(await collection.inputValue() === "", "Approved New product inherited the old assignment.");
            await collection.selectOption(changed);
            await discard(() => tab.getByRole("button", { name: copy.refresh, exact: true }).click(), false);
            assert(await collection.inputValue() === changed, "Declining Refresh lost a new product's draft.");
            await discard(() => tab.getByRole("button", { name: copy.refresh, exact: true }).click(), true);
            await collection.waitFor({ state: "hidden" });
            await book.click();
            await collection.waitFor();
            await collection.selectOption(changed);
            const beforeSave = warnings.length;
            await tab.getByRole("button", { name: copy.save, exact: true }).click();
            await tab.locator(".store-admin-section .admin-feedback.is-success").waitFor();
            assert(warnings.length === beforeSave, "Saving unnecessarily prompted to discard edits.");
            assert(await collection.inputValue() === changed, "Saving did not retain the changed assignment.");
            await buddy.click();
            await tab.getByRole("heading", { name: "Toetstjommie", exact: true }).waitFor();
            assert(warnings.length === beforeSave, "Switching a clean editor prompted to discard changes.");
        });
    }
    currentSuite = "analytics";
    async function recordWinkelEvents(tab, context) {
        const events = [];
        const captureUrl = fixture + "/i/v0/e/";
        await context.route(captureUrl, async route => {
            if (route.request().method() === "POST") events.push(route.request().postDataJSON());
            await route.fulfill({ status: 200, body: '{"status":1}', headers: {
                "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Methods": "POST, OPTIONS",
                "Access-Control-Allow-Headers": "content-type",
            } });
        });
        const captureDuring = async (name, action) => {
            const [response] = await Promise.all([
                tab.waitForResponse(response => response.url() === captureUrl && response.request().method() === "POST" &&
                    response.request().postDataJSON().event === name),
                action(),
            ]);
            assert(response.ok(), `Capture failed for ${name}.`);
            return response.request().postDataJSON();
        };
        return { events, captureDuring };
    }

    await check("Winkel analytics connects discovery, basket and checkout without leaking personal data", async (tab, context) => {
        const { events, captureDuring } = await recordWinkelEvents(tab, context);
        const visit = await captureDuring("winkel_visited", () => tab.reload());
        const detail = await captureDuring("winkel_product_viewed", () => tab.locator('[data-store-card][data-slug="tiekie"] .winkel-product-media').click());
        assert(detail.properties.view_type === "detail" && detail.properties.product_slug === "tiekie", "Detail view attribution is missing.");
        await tab.locator(".winkel-home-link").click();
        const privateSearch = "search-private@example.invalid";
        const search = await captureDuring("winkel_searched", () => tab.locator("[data-store-search]").fill(privateSearch));
        assert(search.properties.query_length === privateSearch.length && search.properties.result_count === 0, "Search counts are incorrect.");
        const reset = await captureDuring("winkel_filter_changed", () => tab.locator("[data-store-reset-filters]").click());
        assert(reset.properties.filter_type === "reset", "Resetting filters did not record a reset.");
        const filter = await captureDuring("winkel_filter_changed", () => tab.locator('[data-store-collection][value="storietjommies"]').check());
        assert(filter.properties.filter_type === "collection" && filter.properties.filter_value === "storietjommies", "Collection filter is missing.");
        const quickView = await captureDuring("winkel_product_viewed", () => tab.locator('[data-store-open-product="tiekie"]').click());
        assert(quickView.properties.view_type === "quick_view", "Quick views must be distinguishable from product details.");
        await tab.locator("[data-store-quick-quantity]").fill("11");
        await tab.locator("[data-store-quick-add]").click();
        await tab.locator("[data-store-quick-error]").waitFor({ state: "visible" });
        await tab.locator("[data-store-quick-quantity]").fill("2");
        const added = await captureDuring("winkel_basket_added", () => tab.locator("[data-store-quick-add]").click());
        assert(added.properties.quantity === 2 && added.properties.collection === "storietjommies", "Successful basket addition is incorrect.");
        assert(events.filter(event => event.event === "winkel_basket_added").length === 1, "An invalid basket add counted as successful.");
        const started = await captureDuring("winkel_checkout_started", () => checkout(tab));
        assert(started.properties.item_count === 2 && started.properties.currency === "ZAR", "Checkout totals are missing.");
        await details(tab);
        const reference = await submit(tab);
        const orders = await (await tab.request.get(fixture + "/_test/orders")).json();
        assert(`winkel:${orders.find(order => order.reference === reference).analytics_visitor_id}` === visit.distinct_id,
            "Checkout did not persist the browser analytics ID on the order.");
        await pay(tab, reference);
        assert(events.every(event => event.distinct_id === visit.distinct_id && event.properties.environment === "development" &&
            event.properties.source === "browser" && event.properties.$process_person_profile === false), "Browser event identity or environment changed.");
        assert(events.every(event => event.event !== "winkel_purchase_verified"), "The receipt generated a client-side purchase event.");
        const payloads = JSON.stringify(events);
        assert(![privateSearch, "review@example.invalid", "PUDO Toetslocker", "0821234567", reference].some(value => payloads.includes(value)),
            "The analytics payload contains personal checkout or search data.");
    });

    await check("Winkel analytics counts rendered pages once during enhanced navigation", async (tab, context) => {
        const { events, captureDuring } = await recordWinkelEvents(tab, context);
        await captureDuring("winkel_visited", () => tab.reload());
        const productUrl = base + "/winkel/produk/tiekie";
        let releaseProduct;
        const waitForProduct = new Promise(resolve => { releaseProduct = resolve; });
        await tab.route(productUrl, async route => {
            await waitForProduct;
            await route.continue();
        }, { times: 1 });
        const openingProduct = captureDuring("winkel_product_viewed",
            () => tab.locator('[data-store-card][data-slug="tiekie"] .winkel-product-media').click());
        try {
            await tab.waitForFunction(() => location.pathname === "/winkel/produk/tiekie" &&
                document.querySelector('[data-store-view="catalog"]'));
            // Exercise an unrelated DOM update while the URL points at the next page.
            await tab.evaluate(() => document.body.appendChild(document.createComment("navigation in progress")));
        } finally {
            releaseProduct();
        }
        await openingProduct;
        await captureDuring("winkel_visited", () => tab.locator(".winkel-home-link").click());
        await tab.locator("[data-store-search]").waitFor();
        await add(tab, "tiekie");
        await captureDuring("winkel_checkout_started", () => checkout(tab));

        const pages = events.filter(event => event.event === "winkel_visited").map(event => event.properties.page);
        const expected = ["catalog", "product", "catalog", "cart", "checkout"];
        assert(JSON.stringify(pages) === JSON.stringify(expected), `Unexpected tracked page sequence: ${pages.join(", ")}.`);
        const detailViews = () => events.filter(event => event.event === "winkel_product_viewed" && event.properties.view_type === "detail");
        assert(detailViews().length === 1, "Opening one product sent more than one detail view.");
        assert(events.filter(event => event.event === "winkel_checkout_started").length === 1, "One checkout sent more than one start event.");

        await captureDuring("winkel_visited", () => tab.goBack());
        await captureDuring("winkel_checkout_started", () => tab.goForward());
        const revisitedPages = events.filter(event => event.event === "winkel_visited").map(event => event.properties.page);
        assert(JSON.stringify(revisitedPages) === JSON.stringify([...expected, "cart", "checkout"]),
            `Back and forward did not count one visit each: ${revisitedPages.join(", ")}.`);
    });
    currentSuite = "orders";
    await check("admin can inspect guest orders and record one shipment from desktop or mobile", async (tab, context) => {
        await add(tab, "tiekie", 2);
        await checkout(tab);
        await details(tab, "Paid Guest");
        const paidReference = await submit(tab);
        await pay(tab, paidReference);
        await tab.locator(".winkel-home-link").click();
        await add(tab, "tiekie");
        await checkout(tab);
        await details(tab, "Pending Guest");
        const pendingReference = await submit(tab);

        const login = await tab.request.post(base + "/api/auth/login", {
            data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" },
        });
        assert(login.ok(), "Local admin sign-in failed.");
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Winkel", exact: true }).click();
        const orders = tab.locator(".store-orders");
        const paidRow = orders.getByRole("row").filter({ hasText: paidReference });
        await paidRow.getByText("Versending nie aangeteken nie", { exact: true }).waitFor();
        await paidRow.getByText("Paid Guest", { exact: true }).click();
        const dialog = tab.getByRole("dialog");
        await dialog.getByText("PUDO Toetslocker", { exact: true }).waitFor();
        await dialog.getByText("review@example.invalid", { exact: true }).waitFor();
        await dialog.getByText(/Tiekie.*× 2/i).waitFor();

        const other = await context.newPage();
        await other.setViewportSize({ width: 390, height: 844 });
        await tab.evaluate(() => localStorage.setItem("shink.admin.language", "en"));
        await other.goto(base + "/admin");
        await other.getByRole("tab", { name: "Shop", exact: true }).click();
        const mobileOrders = other.locator(".store-orders");
        await mobileOrders.getByRole("button", { name: paidReference, exact: true }).click();
        const mobileDialog = other.getByRole("dialog");
        const mobileShip = mobileDialog.getByRole("button", { name: "Mark as shipped", exact: true });
        await mobileShip.waitFor();
        assert(await mobileDialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1), "The mobile order dialog overflows horizontally.");

        await dialog.getByRole("button", { name: "Merk as versend", exact: true }).click();
        await dialog.locator(".store-order-shipped-date").waitFor();
        const readOrders = async () => (await (await tab.request.get(fixture + "/_test/orders")).json());
        const firstShipment = (await readOrders()).find(order => order.reference === paidReference);
        assert(firstShipment.status === "paid" && firstShipment.shipped_at, "Shipment was not persisted independently of payment.");
        await mobileShip.click();
        await mobileDialog.locator(".store-order-shipped-date").waitFor();
        const retried = (await readOrders()).find(order => order.reference === paidReference);
        assert(retried.shipped_at === firstShipment.shipped_at, "A second admin overwrote the shipment timestamp.");

        await mobileDialog.getByRole("button", { name: "Close", exact: true }).click();
        await other.reload();
        await other.getByRole("tab", { name: "Shop", exact: true }).click();
        await mobileOrders.getByRole("row").filter({ hasText: paidReference }).getByText("Shipped", { exact: true }).waitFor();
        await mobileOrders.getByRole("button", { name: "Refresh orders", exact: true }).click();
        await mobileOrders.getByRole("button", { name: pendingReference, exact: true }).click();
        await mobileDialog.getByText("Pending", { exact: true }).waitFor();
        assert(await mobileDialog.getByRole("button", { name: "Mark as shipped", exact: true }).count() === 0,
            "An unpaid order can be marked as shipped.");
        await mobileDialog.getByRole("button", { name: "Close", exact: true }).click();
        await other.setViewportSize({ width: 320, height: 844 });
        await mobileOrders.getByRole("button", { name: "Next page", exact: true }).click();
        const legacyOrder = mobileOrders.getByRole("row").filter({ hasText: "fixture-older-00" });
        await legacyOrder.getByText("Shipment not recorded", { exact: true }).waitFor();
        await legacyOrder.getByRole("button", { name: "fixture-older-00", exact: true }).click();
        await mobileDialog.getByText("Shipment not recorded", { exact: true }).waitFor();
        await mobileDialog.getByText("LongProductName".repeat(9) + " × 1", { exact: true }).waitFor();
        assert(await mobileDialog.locator(".mud-dialog-content").evaluate(element => element.scrollWidth <= element.clientWidth + 1),
            "A long product name overflows the order dialog on a narrow screen.");
    });

    await check("overlapping order refreshes keep the current page", async (tab) => {
        const login = await tab.request.post(base + "/api/auth/login", {
            data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" },
        });
        assert(login.ok(), "Local admin sign-in failed.");
        await tab.evaluate(() => localStorage.setItem("shink.admin.language", "en"));
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Shop", exact: true }).click();
        const orders = tab.locator(".store-orders");
        await orders.getByRole("button", { name: "Next page", exact: true }).click();
        const legacyOrder = orders.getByRole("row").filter({ hasText: "fixture-older-00" });
        await legacyOrder.getByText("Older Guest", { exact: true }).waitFor();
        const pageNumber = orders.locator(".mud-table-page-number-information");
        const initialPage = await pageNumber.innerText();
        const refresh = orders.getByRole("button", { name: "Refresh orders", exact: true });
        await tab.request.post(fixture + "/_test/pause-order-page", {
            data: { reference: "fixture-older-00", customer_name: "Refreshed Guest" },
        });
        try {
            await refresh.click();
            const paused = await (await tab.request.get(fixture + "/_test/order-page-paused")).json();
            assert(paused.paused, "The first refresh never reached the fixture.");
            await refresh.click();
            await legacyOrder.getByText("Refreshed Guest", { exact: true }).waitFor();
            assert(await pageNumber.innerText() === initialPage, "A cancelled refresh reset the current orders page.");
        } finally {
            await tab.request.post(fixture + "/_test/release-order-page");
        }
    });

    await check("a failed order refresh keeps the page and reports the error until retry", async (tab) => {
        const login = await tab.request.post(base + "/api/auth/login", {
            data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" },
        });
        assert(login.ok(), "Local admin sign-in failed.");
        await tab.evaluate(() => localStorage.setItem("shink.admin.language", "en"));
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Shop", exact: true }).click();
        const orders = tab.locator(".store-orders");
        const legacyOrder = orders.getByRole("button", { name: "fixture-older-00", exact: true });
        await orders.getByRole("button", { name: "Next page", exact: true }).click();
        await legacyOrder.waitFor();
        const pageNumber = orders.locator(".mud-table-page-number-information");
        const initialPage = await pageNumber.innerText();
        const refresh = orders.getByRole("button", { name: "Refresh orders", exact: true });
        await tab.request.post(fixture + "/_test/fail-order-page");
        await refresh.click();
        const error = orders.getByRole("alert").filter({ hasText: "Could not load shop orders." });
        await error.waitFor();
        assert(await pageNumber.innerText() === initialPage, "A failed refresh reset the current orders page.");
        assert(await legacyOrder.isVisible(), "A failed refresh removed the last loaded rows.");
        await refresh.click();
        await error.waitFor({ state: "hidden" });
        assert(await pageNumber.innerText() === initialPage && await legacyOrder.isVisible(),
            "Retrying the refresh did not restore the current orders page.");

        await tab.request.post(fixture + "/_test/fail-order-page");
        await orders.getByRole("button", { name: "Previous page", exact: true }).click();
        await error.waitFor();
        await legacyOrder.waitFor({ state: "hidden" });
        await refresh.click();
        await error.waitFor({ state: "hidden" });
        const newer = orders.getByRole("button", { name: "fixture-older-25", exact: true });
        await newer.waitFor();

        await tab.request.post(fixture + "/_test/admin-access", { data: { enabled: false } });
        try {
            await refresh.click();
            await error.waitFor();
            await newer.waitFor({ state: "hidden" });
            assert(await orders.locator(".store-order-reference").count() === 0,
                "The grid kept customer rows after access was revoked.");
        } finally {
            await tab.request.post(fixture + "/_test/admin-access", { data: { enabled: true } });
        }
    });

    await check("the orders pager has Afrikaans names and navigates on a narrow screen", async (tab) => {
        const login = await tab.request.post(base + "/api/auth/login", {
            data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" },
        });
        assert(login.ok(), "Local admin sign-in failed.");
        await tab.setViewportSize({ width: 320, height: 844 });
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Winkel", exact: true }).click();
        const orders = tab.locator(".store-orders");
        const first = orders.getByRole("button", { name: "Eerste bladsy", exact: true });
        const previous = orders.getByRole("button", { name: "Vorige bladsy", exact: true });
        const next = orders.getByRole("button", { name: "Volgende bladsy", exact: true });
        const last = orders.getByRole("button", { name: "Laaste bladsy", exact: true });
        const oldest = orders.getByRole("button", { name: "fixture-older-00", exact: true });
        const newer = orders.getByRole("button", { name: "fixture-older-25", exact: true });
        await newer.waitFor();
        assert(await first.isDisabled() && await previous.isDisabled(), "Back navigation is available on the first page.");
        await last.click();
        await oldest.waitFor();
        assert(await next.isDisabled() && await last.isDisabled(), "Forward navigation is available on the last page.");
        await first.click();
        await newer.waitFor();
        await next.click();
        await oldest.waitFor();
        await previous.click();
        await newer.waitFor();

        const orderCount = (await (await tab.request.get(fixture + "/_test/orders")).json()).length;
        await orders.locator(".mud-table-pagination-select").click();
        await tab.locator(".mud-popover-open").getByText("10", { exact: true }).click();
        await orders.getByText(`1–10 van ${orderCount}`, { exact: true }).waitFor();
        await last.click();
        await oldest.waitFor();
        await orders.getByText(`21–${orderCount} van ${orderCount}`, { exact: true }).waitFor();
        assert(await orders.evaluate(element => element.scrollWidth <= element.clientWidth + 1),
            "The orders grid or pager overflows on a narrow screen.");
    });

    async function openOrdersOnSecondPage(tab) {
        const login = await tab.request.post(base + "/api/auth/login", {
            data: { email: "admin@example.invalid", password: "Local-checkout-test-123!" },
        });
        assert(login.ok(), "Local admin sign-in failed.");
        await tab.evaluate(() => localStorage.setItem("shink.admin.language", "en"));
        await tab.goto(base + "/admin");
        await tab.getByRole("tab", { name: "Shop", exact: true }).click();
        const orders = tab.locator(".store-orders");
        await orders.getByRole("button", { name: "Next page", exact: true }).click();
        await orders.getByRole("button", { name: "fixture-older-00", exact: true }).waitFor();
        return orders;
    }

    await check("a temporary admin lookup outage leaves the current orders page in place", async (tab) => {
        const orders = await openOrdersOnSecondPage(tab);
        const pageNumber = orders.locator(".mud-table-page-number-information");
        const initialPage = await pageNumber.innerText();
        const refresh = orders.getByRole("button", { name: "Refresh orders", exact: true });
        const error = orders.getByRole("alert").filter({ hasText: "Could not load shop orders." });

        const armed = await tab.request.post(fixture + "/_test/fail-admin-lookup");
        assert(armed.ok(), "Could not set up the admin lookup outage.");
        await refresh.click();
        await error.waitFor();
        assert(await pageNumber.innerText() === initialPage, "An admin lookup outage reset the order page.");
        assert(await orders.getByRole("button", { name: "fixture-older-00", exact: true }).isVisible(),
            "An admin lookup outage removed the previously loaded rows.");
        await refresh.click();
        await error.waitFor({ state: "hidden" });
        assert(await pageNumber.innerText() === initialPage, "Retrying the admin lookup moved the order page.");
    });

    await check("a saved shipment updates the grid even if the list refresh fails", async (tab) => {
        const orders = await openOrdersOnSecondPage(tab);
        const row = orders.getByRole("row").filter({ hasText: "fixture-older-00" });
        await row.getByRole("button", { name: "fixture-older-00", exact: true }).click();
        const dialog = tab.getByRole("dialog");
        const ship = dialog.getByRole("button", { name: "Mark as shipped", exact: true });
        await ship.waitFor();

        const armed = await tab.request.post(fixture + "/_test/fail-order-page");
        assert(armed.ok(), "Could not set up the order list outage.");
        await ship.click();
        await dialog.locator(".store-order-shipped-date").waitFor();
        await dialog.getByRole("button", { name: "Close", exact: true }).click();
        await orders.getByRole("alert").filter({ hasText: "Could not load shop orders." }).waitFor();
        const savedOrders = await (await tab.request.get(fixture + "/_test/orders")).json();
        assert(savedOrders.find(order => order.reference === "fixture-older-00")?.shipped_at,
            "The fixture did not record the shipment.");
        await row.getByText("Shipped", { exact: true }).waitFor();
    });

    await check("the grid recovers when the last orders page disappears at a page boundary", async (tab) => {
        const orders = await openOrdersOnSecondPage(tab);
        const trimmed = await tab.request.post(fixture + "/_test/trim-orders", { data: { remaining: 25 } });
        assert(trimmed.ok(), "Could not reduce the order count.");
        await orders.getByRole("button", { name: "Refresh orders", exact: true }).click();
        await orders.getByText("1–25 of 25", { exact: true }).waitFor();
        await orders.getByRole("button", { name: "fixture-older-00", exact: true }).waitFor();
        assert(await orders.locator(".store-order-reference").count() === 25,
            "The grid did not load the remaining orders.");
        assert(await orders.getByRole("button", { name: "Next page", exact: true }).isDisabled(),
            "The pager still offers a page beyond the order count.");
    });

    const summary = { passed: results.filter(result => result.passed).length, failed: results.filter(result => !result.passed).length, results };
    if (summary.failed) throw new Error(JSON.stringify(summary));
    return summary;
}
