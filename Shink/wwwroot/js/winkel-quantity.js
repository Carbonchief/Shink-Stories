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

    function getSuggestionValue(suggestion, key) {
        if (!suggestion) {
            return "";
        }

        var pascalKey = key.charAt(0).toUpperCase() + key.slice(1);
        return suggestion[key] || suggestion[pascalKey] || "";
    }

    function setFieldValue(form, selector, value) {
        var field = form.querySelector(selector);
        if (!field || !value) {
            return;
        }

        field.value = value;
        dispatchChange(field);
    }

    function hideAddressSuggestions(suggestionsPanel) {
        if (!suggestionsPanel) {
            return;
        }

        suggestionsPanel.hidden = true;
        suggestionsPanel.innerHTML = "";
        suggestionsPanel.__winkelAddressSuggestions = [];
    }

    function buildSuggestionMeta(suggestion, label, line1) {
        var parts = [
            getSuggestionValue(suggestion, "suburb"),
            getSuggestionValue(suggestion, "city"),
            getSuggestionValue(suggestion, "postalCode")
        ].filter(function (value) {
            return value && value.length > 0;
        });

        if (label && line1 && label !== line1) {
            return label;
        }

        return parts.join(", ");
    }

    function renderAddressSuggestions(input, suggestionsPanel, suggestions) {
        hideAddressSuggestions(suggestionsPanel);
        if (!suggestions || suggestions.length === 0) {
            input.setAttribute("aria-expanded", "false");
            return;
        }

        var fragment = document.createDocumentFragment();
        suggestions.forEach(function (suggestion, index) {
            var label = getSuggestionValue(suggestion, "label");
            var line1 = getSuggestionValue(suggestion, "addressLine1") || label;
            var meta = buildSuggestionMeta(suggestion, label, line1);
            if (!line1) {
                return;
            }

            var button = document.createElement("button");
            button.type = "button";
            button.className = "winkel-address-suggestion";
            button.setAttribute("role", "option");
            button.setAttribute("data-winkel-address-suggestion", index.toString());

            var main = document.createElement("span");
            main.className = "winkel-address-suggestion-main";
            main.textContent = line1;
            button.appendChild(main);

            if (meta) {
                var metaElement = document.createElement("span");
                metaElement.className = "winkel-address-suggestion-meta";
                metaElement.textContent = meta;
                button.appendChild(metaElement);
            }

            fragment.appendChild(button);
        });

        if (!fragment.childNodes.length) {
            input.setAttribute("aria-expanded", "false");
            return;
        }

        suggestionsPanel.__winkelAddressSuggestions = suggestions;
        suggestionsPanel.appendChild(fragment);
        suggestionsPanel.hidden = false;
        input.setAttribute("aria-expanded", "true");
    }

    function applyAddressSuggestion(input, suggestion) {
        var form = closest(input, "#winkel-order-form");
        if (!form || !suggestion) {
            return;
        }

        var line1 = getSuggestionValue(suggestion, "addressLine1") || getSuggestionValue(suggestion, "label");
        input.value = line1;
        dispatchChange(input);

        setFieldValue(form, "[data-winkel-address-postal-code]", getSuggestionValue(suggestion, "postalCode"));
        setFieldValue(form, "[data-winkel-address-suburb]", getSuggestionValue(suggestion, "suburb"));
        setFieldValue(form, "[data-winkel-address-city]", getSuggestionValue(suggestion, "city"));
    }

    var addressAutocompleteTimer = null;
    var addressAutocompleteVersion = 0;

    function scheduleAddressAutocomplete(input) {
        var form = closest(input, "#winkel-order-form");
        var suggestionsPanel = form && form.querySelector("[data-winkel-address-suggestions]");
        var query = input.value.trim();
        addressAutocompleteVersion += 1;
        var requestVersion = addressAutocompleteVersion;

        if (addressAutocompleteTimer !== null) {
            window.clearTimeout(addressAutocompleteTimer);
            addressAutocompleteTimer = null;
        }

        if (!suggestionsPanel || query.length < 3 || typeof fetch !== "function") {
            hideAddressSuggestions(suggestionsPanel);
            input.setAttribute("aria-expanded", "false");
            return;
        }

        addressAutocompleteTimer = window.setTimeout(function () {
            addressAutocompleteTimer = null;
            var url = "/api/winkel/address-autocomplete?q=" + encodeURIComponent(query) + "&limit=5";
            fetch(url, {
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json"
                }
            })
                .then(function (response) {
                    if (!response.ok) {
                        return { results: [] };
                    }

                    return response.json();
                })
                .then(function (payload) {
                    if (requestVersion !== addressAutocompleteVersion) {
                        return;
                    }

                    var suggestions = payload.results || payload.Results || [];
                    renderAddressSuggestions(input, suggestionsPanel, suggestions);
                })
                .catch(function () {
                    if (requestVersion === addressAutocompleteVersion) {
                        hideAddressSuggestions(suggestionsPanel);
                        input.setAttribute("aria-expanded", "false");
                    }
                });
        }, 260);
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

    document.addEventListener("input", function (event) {
        if (!matches(event.target, "[data-winkel-address-input]")) {
            return;
        }

        scheduleAddressAutocomplete(event.target);
    });

    document.addEventListener("click", function (event) {
        var suggestionButton = closest(event.target, "[data-winkel-address-suggestion]");
        if (!suggestionButton) {
            var openPanel = document.querySelector("[data-winkel-address-suggestions]:not([hidden])");
            if (openPanel && !closest(event.target, ".winkel-field")) {
                hideAddressSuggestions(openPanel);
            }
            return;
        }

        var suggestionsPanel = closest(suggestionButton, "[data-winkel-address-suggestions]");
        var form = closest(suggestionButton, "#winkel-order-form");
        var input = form && form.querySelector("[data-winkel-address-input]");
        var suggestions = suggestionsPanel && suggestionsPanel.__winkelAddressSuggestions;
        var index = toNumber(suggestionButton.getAttribute("data-winkel-address-suggestion"), -1);
        if (!input || !suggestions || index < 0 || !suggestions[index]) {
            return;
        }

        applyAddressSuggestion(input, suggestions[index]);
        hideAddressSuggestions(suggestionsPanel);
        input.setAttribute("aria-expanded", "false");
        input.focus();
    });

    document.addEventListener("keydown", function (event) {
        if (!matches(event.target, "[data-winkel-address-input]") || event.key !== "Escape") {
            return;
        }

        var form = closest(event.target, "#winkel-order-form");
        hideAddressSuggestions(form && form.querySelector("[data-winkel-address-suggestions]"));
        event.target.setAttribute("aria-expanded", "false");
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
