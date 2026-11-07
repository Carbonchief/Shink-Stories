import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";

// Import the shipped module directly without changing the repository's module mode.
const source = await readFile(
    new URL(
        "../../Shink/Components/Store/StoreShell.razor.js",
        import.meta.url,
    ),
    "utf8",
);
const {
    normalizeCart,
    calculateTotals,
    changeQuantity,
    captureCheckout,
    replaceCartItems,
    completePurchase,
} = await import(
    `data:text/javascript;base64,${Buffer.from(source).toString("base64")}`
);
const products = [
    { slug: "tiekie", price: 25000, freeDelivery: false },
    { slug: "georgie", price: 25000, freeDelivery: false },
    { slug: "promotion", price: 30000, freeDelivery: true },
];

test("an empty basket has no delivery fee", () => {
    assert.deepEqual(calculateTotals([], products), {
        subtotal: 0,
        count: 0,
        delivery: 0,
        deliveryIncluded: false,
        total: 0,
    });
});
test("mixed products incur exactly one R80 delivery fee", () => {
    const totals = calculateTotals(
        [
            { slug: "tiekie", quantity: 2 },
            { slug: "georgie", quantity: 3 },
        ],
        products,
    );
    assert.equal(totals.count, 5);
    assert.equal(totals.subtotal, 125000);
    assert.equal(totals.delivery, 8000);
    assert.equal(totals.total, 133000);
});
test("a qualifying product waives delivery for the whole basket", () => {
    const items = [
        { slug: "tiekie", quantity: 2 },
        { slug: "promotion", quantity: 1 },
    ];
    assert.equal(calculateTotals(items, products).total, 80000);
    const result = changeQuantity(items, "promotion", 0, products);
    assert.equal(calculateTotals(result.items, products).total, 58000);
});
test("stored prices cannot override current catalogue prices", () => {
    assert.equal(
        calculateTotals([{ slug: "tiekie", quantity: 1, price: 1 }], products)
            .total,
        33000,
    );
});
test("unavailable products, invalid quantities and malformed storage are discarded", () => {
    assert.deepEqual(
        normalizeCart(
            [
                { slug: "removed", quantity: 2 },
                { slug: "tiekie", quantity: -1 },
                { slug: "georgie", quantity: 1.5 },
                null,
            ],
            products,
        ),
        [],
    );
    assert.deepEqual(normalizeCart({ items: "broken" }, products), []);
});
test("duplicate and oversized persisted quantities respect both limits", () => {
    const items = normalizeCart(
        [
            { slug: "tiekie", quantity: 8 },
            { slug: "tiekie", quantity: 8 },
            { slug: "georgie", quantity: 100 },
            { slug: "promotion", quantity: 2 },
        ],
        products,
    );
    assert.deepEqual(items, [
        { slug: "tiekie", quantity: 10 },
        { slug: "georgie", quantity: 10 },
    ]);
});
test("a 21st item is rejected without losing the existing basket", () => {
    const items = [
        { slug: "tiekie", quantity: 10 },
        { slug: "georgie", quantity: 10 },
    ];
    const result = changeQuantity(items, "promotion", 1, products);
    assert.ok(result.error);
    assert.deepEqual(result.items, items);
});
test("manual values must be whole numbers within the product limit", () => {
    for (const quantity of [11, -1, 1.5, NaN])
        assert.ok(changeQuantity([], "tiekie", quantity, products).error);
    assert.ok(changeQuantity([], "removed", 1, products).error);
});
test("removing an item leaves the other products intact", () => {
    assert.deepEqual(
        changeQuantity(
            [
                { slug: "tiekie", quantity: 2 },
                { slug: "georgie", quantity: 1 },
            ],
            "tiekie",
            0,
            products,
        ).items,
        [{ slug: "georgie", quantity: 1 }],
    );
});
test("verified completion removes purchased quantities once and preserves later additions", () => {
    const checkoutId = "11111111111111111111111111111111";
    const submitted = captureCheckout(
        { items: [{ slug: "tiekie", quantity: 2 }], completedReferences: [] },
        checkoutId,
        products,
    );
    const state = replaceCartItems(submitted, [
        { slug: "tiekie", quantity: 3 },
        { slug: "georgie", quantity: 1 },
    ]);
    const purchased = [{ slug: "tiekie", quantity: 2 }];
    const completed = completePurchase(
        state,
        "W123",
        purchased,
        products,
        checkoutId,
    );
    assert.deepEqual(completed.items, [
        { slug: "tiekie", quantity: 1 },
        { slug: "georgie", quantity: 1 },
    ]);
    assert.deepEqual(
        completePurchase(completed, "W123", purchased, products, checkoutId),
        completed,
    );
    assert.deepEqual(completePurchase(state, "", purchased, products), state);
});

test("returning from payment preserves a basket that was cleared and rebuilt", () => {
    const checkoutId = "11111111111111111111111111111111";
    const state = {
        items: [{ slug: "tiekie", quantity: 1 }],
        completedReferences: [],
        pendingCheckouts: [{ id: checkoutId, items: [] }],
    };
    const completed = completePurchase(
        state,
        "W-OLD",
        [{ slug: "tiekie", quantity: 2 }],
        products,
        checkoutId,
    );
    assert.deepEqual(completed.items, [{ slug: "tiekie", quantity: 1 }]);
});

test("a receipt without a matching checkout cannot remove current basket items", () => {
    const state = {
        items: [{ slug: "tiekie", quantity: 1 }],
        completedReferences: [],
        pendingCheckouts: [],
    };
    const completed = completePurchase(
        state,
        "W-RECOVERED",
        [{ slug: "tiekie", quantity: 1 }],
        products,
    );
    assert.deepEqual(completed.items, state.items);
});

test("clearing the basket retires its submitted quantities before the same product is re-added", () => {
    const id = "11111111111111111111111111111111";
    const pending = captureCheckout(
        { items: [{ slug: "tiekie", quantity: 2 }], completedReferences: [] },
        id,
        products,
    );
    const cleared = replaceCartItems(pending, []);
    const rebuilt = replaceCartItems(cleared, [
        { slug: "tiekie", quantity: 1 },
    ]);
    assert.deepEqual(
        completePurchase(
            rebuilt,
            "W-OLD",
            [{ slug: "tiekie", quantity: 2 }],
            products,
            id,
        ).items,
        rebuilt.items,
    );
});

test("removing and re-adding one product preserves it while completing the other purchased product", () => {
    const id = "11111111111111111111111111111111";
    const original = [
        { slug: "tiekie", quantity: 2 },
        { slug: "georgie", quantity: 1 },
    ];
    const pending = captureCheckout(
        { items: original, completedReferences: [] },
        id,
        products,
    );
    const removed = replaceCartItems(pending, [
        { slug: "georgie", quantity: 1 },
    ]);
    const rebuilt = replaceCartItems(removed, [
        { slug: "tiekie", quantity: 1 },
        { slug: "georgie", quantity: 1 },
    ]);
    assert.deepEqual(
        completePurchase(rebuilt, "W-OLD", original, products, id).items,
        [{ slug: "tiekie", quantity: 1 }],
    );
});

test("reducing then increasing a quantity keeps the later addition after payment", () => {
    const id = "11111111111111111111111111111111";
    const pending = captureCheckout(
        { items: [{ slug: "tiekie", quantity: 2 }], completedReferences: [] },
        id,
        products,
    );
    const reduced = replaceCartItems(pending, [
        { slug: "tiekie", quantity: 1 },
    ]);
    const increased = replaceCartItems(reduced, [
        { slug: "tiekie", quantity: 2 },
    ]);
    assert.deepEqual(
        completePurchase(
            increased,
            "W-OLD",
            [{ slug: "tiekie", quantity: 2 }],
            products,
            id,
        ).items,
        [{ slug: "tiekie", quantity: 1 }],
    );
});

test("overlapping purchases cannot deduct a newly added item after the first completion", () => {
    const firstId = "11111111111111111111111111111111",
        secondId = "22222222222222222222222222222222";
    const original = [{ slug: "tiekie", quantity: 1 }];
    const first = captureCheckout(
        { items: original, completedReferences: [] },
        firstId,
        products,
    );
    const both = captureCheckout(first, secondId, products);
    const completed = completePurchase(
        both,
        "W-FIRST",
        original,
        products,
        firstId,
    );
    const newBasket = replaceCartItems(completed, original);
    assert.deepEqual(
        completePurchase(newBasket, "W-SECOND", original, products, secondId)
            .items,
        original,
    );
});

test("an unknown checkout identity cannot borrow another pending basket", () => {
    const pending = captureCheckout(
        { items: [{ slug: "tiekie", quantity: 1 }], completedReferences: [] },
        "11111111111111111111111111111111",
        products,
    );
    assert.deepEqual(
        completePurchase(
            pending,
            "W-OTHER",
            pending.items,
            products,
            "22222222222222222222222222222222",
        ).items,
        pending.items,
    );
});
