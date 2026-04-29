(function () {
    function toNumber(value, fallback) {
        var parsed = parseInt(value, 10);
        return isNaN(parsed) ? fallback : parsed;
    }

    function clamp(input, value) {
        var min = toNumber(input.min, 0);
        var max = toNumber(input.max, 10);
        return Math.min(max, Math.max(min, value));
    }

    function matches(element, selector) {
        var matcher = element.matches || element.msMatchesSelector || element.webkitMatchesSelector;
        return !!matcher && matcher.call(element, selector);
    }

    function closest(element, selector) {
        while (element && element.nodeType === 1) {
            if (matches(element, selector)) {
                return element;
            }

            element = element.parentElement;
        }

        return null;
    }

    function updateButtons(group) {
        var input = group.querySelector("[data-winkel-quantity-input]");
        if (!input) {
            return;
        }

        var value = clamp(input, toNumber(input.value, 0));
        input.value = value.toString();

        var decrement = group.querySelector("[data-winkel-quantity-decrement]");
        var increment = group.querySelector("[data-winkel-quantity-increment]");
        var min = toNumber(input.min, 0);
        var max = toNumber(input.max, 10);

        if (decrement) {
            decrement.disabled = value <= min;
        }

        if (increment) {
            increment.disabled = value >= max;
        }
    }

    function updateAllButtons() {
        Array.prototype.forEach.call(document.querySelectorAll("[data-winkel-quantity]"), updateButtons);
    }

    function dispatchChange(input) {
        var event;
        if (typeof Event === "function") {
            event = new Event("change", { bubbles: true });
        } else {
            event = document.createEvent("Event");
            event.initEvent("change", true, false);
        }

        input.dispatchEvent(event);
    }

    document.addEventListener("click", function (event) {
        var button = closest(event.target, "[data-winkel-quantity-increment], [data-winkel-quantity-decrement]");
        if (!button) {
            return;
        }

        var group = closest(button, "[data-winkel-quantity]");
        var input = group && group.querySelector("[data-winkel-quantity-input]");
        if (!input) {
            return;
        }

        var direction = button.hasAttribute("data-winkel-quantity-increment") ? 1 : -1;
        input.value = clamp(input, toNumber(input.value, 0) + direction).toString();
        dispatchChange(input);
        updateButtons(group);
    });

    document.addEventListener("input", function (event) {
        if (!matches(event.target, "[data-winkel-quantity-input]")) {
            return;
        }

        var group = closest(event.target, "[data-winkel-quantity]");
        if (group) {
            updateButtons(group);
        }
    });

    document.addEventListener("blur", function (event) {
        if (!matches(event.target, "[data-winkel-quantity-input]")) {
            return;
        }

        var group = closest(event.target, "[data-winkel-quantity]");
        if (group) {
            updateButtons(group);
        }
    }, true);

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", updateAllButtons);
    } else {
        updateAllButtons();
    }
})();
