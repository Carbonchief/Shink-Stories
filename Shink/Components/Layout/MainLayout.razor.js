const NAV_TOGGLE_SELECTOR = "[data-nav-menu-toggle]";
const NAV_TOGGLE_LABEL_SELECTOR = "[data-nav-menu-toggle-label]";
const SEARCH_FORM_SELECTOR = "[data-search-form]";
const SEARCH_TOGGLE_SELECTOR = "[data-search-toggle]";
const SEARCH_INPUT_SELECTOR = "[data-search-input]";
const SEARCH_SUGGESTIONS_SELECTOR = "[data-search-suggestions]";
const SEARCH_SUGGESTIONS_LIST_SELECTOR = "[data-search-suggestions-list]";
const ACCOUNT_MENU_ROOT_SELECTOR = "[data-account-menu-root]";
const ACCOUNT_TOGGLE_SELECTOR = "[data-account-toggle]";
const ACCOUNT_DROPDOWN_SELECTOR = "[data-account-dropdown]";
const OPEN_CLASS = "is-open";
const SEARCH_ACTIVE_CLASS = "is-search-active";
const SEARCH_SUGGEST_ENDPOINT = "/api/search/suggest";
const SEARCH_MIN_QUERY_LENGTH = 2;
const SEARCH_DEBOUNCE_MS = 160;
const SEARCH_MAX_RESULTS = 8;
const OPEN_LABEL = "Maak navigasie toe";
const CLOSED_LABEL = "Maak navigasie oop";

function closeNavMenuInContainer(container) {
    if (!(container instanceof Element)) {
        return;
    }

    const navMenu = container.querySelector(".site-nav");
    const navToggle = container.querySelector(NAV_TOGGLE_SELECTOR);
    if (!(navMenu instanceof HTMLElement) || !(navToggle instanceof HTMLElement)) {
        return;
    }

    navMenu.classList.remove(OPEN_CLASS);
    navToggle.setAttribute("aria-expanded", "false");
    navToggle.setAttribute("aria-label", CLOSED_LABEL);
    navToggle.setAttribute("title", CLOSED_LABEL);

    const srLabel = navToggle.querySelector(NAV_TOGGLE_LABEL_SELECTOR);
    if (srLabel instanceof HTMLElement) {
        srLabel.textContent = CLOSED_LABEL;
    }
}

function closeAccountMenuInContainer(container) {
    if (!(container instanceof Element)) {
        return;
    }

    const accountRoot = container.querySelector(ACCOUNT_MENU_ROOT_SELECTOR);
    const accountToggle = accountRoot?.querySelector(ACCOUNT_TOGGLE_SELECTOR);
    if (!(accountRoot instanceof HTMLElement) || !(accountToggle instanceof HTMLElement)) {
        return;
    }

    accountRoot.classList.remove(OPEN_CLASS);
    accountToggle.setAttribute("aria-expanded", "false");
}

function wireNavToggle(toggleButton) {
    if (!(toggleButton instanceof HTMLElement) || toggleButton.dataset.navMenuWired === "true") {
        return;
    }

    const navId = toggleButton.getAttribute("aria-controls");
    if (!navId) {
        return;
    }

    const navMenu = document.getElementById(navId);
    if (!(navMenu instanceof HTMLElement)) {
        return;
    }

    const navControls = toggleButton.closest(".nav-controls");
    const srLabel = toggleButton.querySelector(NAV_TOGGLE_LABEL_SELECTOR);

    const setMenuState = (isOpen) => {
        navMenu.classList.toggle(OPEN_CLASS, isOpen);
        toggleButton.setAttribute("aria-expanded", isOpen ? "true" : "false");
        closeAccountMenuInContainer(navControls);

        const label = isOpen ? OPEN_LABEL : CLOSED_LABEL;
        toggleButton.setAttribute("aria-label", label);
        toggleButton.setAttribute("title", label);

        if (srLabel instanceof HTMLElement) {
            srLabel.textContent = label;
        }
    };

    const isOpen = () => navMenu.classList.contains(OPEN_CLASS);

    setMenuState(false);

    toggleButton.addEventListener("click", (event) => {
        event.preventDefault();
        setMenuState(!isOpen());
    });

    navMenu.querySelectorAll("a").forEach((link) => {
        link.addEventListener("click", () => {
            setMenuState(false);
        });
    });

    document.addEventListener("click", (event) => {
        if (!isOpen()) {
            return;
        }

        const target = event.target;
        if (target instanceof Node && navControls instanceof HTMLElement && navControls.contains(target)) {
            return;
        }

        setMenuState(false);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            setMenuState(false);
        }
    });

    document.addEventListener("enhancedload", () => {
        setMenuState(false);
    });

    toggleButton.dataset.navMenuWired = "true";
}

function wireHeaderSearch(searchForm) {
    if (!(searchForm instanceof HTMLFormElement) || searchForm.dataset.searchWired === "true") {
        return;
    }

    const searchToggle = searchForm.querySelector(SEARCH_TOGGLE_SELECTOR);
    const searchInput = searchForm.querySelector(SEARCH_INPUT_SELECTOR);
    const suggestionsPanel = searchForm.querySelector(SEARCH_SUGGESTIONS_SELECTOR);
    const suggestionsList = searchForm.querySelector(SEARCH_SUGGESTIONS_LIST_SELECTOR);
    const controlsContainer = searchForm.closest(".nav-controls, .guest-controls");
    if (!(searchToggle instanceof HTMLButtonElement) ||
        !(searchInput instanceof HTMLInputElement) ||
        !(suggestionsPanel instanceof HTMLElement) ||
        !(suggestionsList instanceof HTMLElement) ||
        !(controlsContainer instanceof HTMLElement)) {
        return;
    }

    let searchDebounceHandle = null;
    let searchRequestVersion = 0;
    let activeAbortController = null;

    const isSearchActive = () => controlsContainer.classList.contains(SEARCH_ACTIVE_CLASS);

    const hideSuggestions = () => {
        suggestionsList.replaceChildren();
        suggestionsPanel.hidden = true;
    };

    const showEmptyState = (message) => {
        const empty = document.createElement("p");
        empty.className = "site-search-suggestion-empty";
        empty.textContent = message;
        suggestionsList.replaceChildren(empty);
        suggestionsPanel.hidden = false;
    };

    const renderSuggestions = (results) => {
        if (!Array.isArray(results) || results.length === 0) {
            showEmptyState("Geen voorstelle gevind nie.");
            return;
        }

        const fragment = document.createDocumentFragment();
        results.forEach((result) => {
            if (!result || typeof result.url !== "string" || typeof result.title !== "string") {
                return;
            }

            const link = document.createElement("a");
            link.className = "site-search-suggestion";
            link.href = result.url;

            const thumbnail = document.createElement("img");
            thumbnail.className = "site-search-suggestion-thumb";
            thumbnail.alt = "";
            thumbnail.loading = "lazy";
            thumbnail.decoding = "async";
            thumbnail.src = typeof result.thumbnailPath === "string" && result.thumbnailPath.length > 0
                ? result.thumbnailPath
                : "/branding/schink-logo-green.png";
            link.append(thumbnail);

            const copy = document.createElement("span");
            copy.className = "site-search-suggestion-copy";

            const title = document.createElement("p");
            title.className = "site-search-suggestion-title";
            title.textContent = result.title;
            copy.append(title);

            if (typeof result.kind === "string" && result.kind.length > 0) {
                const kind = document.createElement("p");
                kind.className = "site-search-suggestion-kind";
                kind.textContent = result.kind;
                copy.append(kind);
            }

            link.append(copy);
            fragment.append(link);
        });

        if (!fragment.hasChildNodes()) {
            showEmptyState("Geen voorstelle gevind nie.");
            return;
        }

        suggestionsList.replaceChildren(fragment);
        suggestionsPanel.hidden = false;
    };

    const clearPendingSearch = () => {
        if (searchDebounceHandle !== null) {
            window.clearTimeout(searchDebounceHandle);
            searchDebounceHandle = null;
        }

        if (activeAbortController instanceof AbortController) {
            activeAbortController.abort();
            activeAbortController = null;
        }
    };

    const fetchSuggestions = async (query) => {
        if (!isSearchActive()) {
            hideSuggestions();
            return;
        }

        const trimmedQuery = query.trim();
        if (trimmedQuery.length < SEARCH_MIN_QUERY_LENGTH) {
            hideSuggestions();
            return;
        }

        const requestVersion = ++searchRequestVersion;
        if (activeAbortController instanceof AbortController) {
            activeAbortController.abort();
        }

        activeAbortController = new AbortController();
        const endpointUrl = `${SEARCH_SUGGEST_ENDPOINT}?q=${encodeURIComponent(trimmedQuery)}&limit=${SEARCH_MAX_RESULTS}`;

        try {
            const response = await fetch(endpointUrl, {
                method: "GET",
                credentials: "same-origin",
                headers: {
                    Accept: "application/json"
                },
                signal: activeAbortController.signal
            });

            if (!response.ok) {
                if (requestVersion === searchRequestVersion) {
                    hideSuggestions();
                }
                return;
            }

            const payload = await response.json();
            if (requestVersion !== searchRequestVersion || !isSearchActive()) {
                return;
            }

            const results = Array.isArray(payload?.results) ? payload.results : [];
            renderSuggestions(results);
        } catch (error) {
            if (error instanceof DOMException && error.name === "AbortError") {
                return;
            }

            if (requestVersion === searchRequestVersion) {
                hideSuggestions();
            }
        }
    };

    const scheduleSuggestionsFetch = (query) => {
        clearPendingSearch();

        const trimmedQuery = query.trim();
        if (trimmedQuery.length < SEARCH_MIN_QUERY_LENGTH) {
            hideSuggestions();
            return;
        }

        searchDebounceHandle = window.setTimeout(() => {
            searchDebounceHandle = null;
            fetchSuggestions(trimmedQuery);
        }, SEARCH_DEBOUNCE_MS);
    };

    const setSearchState = (isActive, options = {}) => {
        const { focusInput = false, focusToggle = false } = options;
        controlsContainer.classList.toggle(SEARCH_ACTIVE_CLASS, isActive);
        searchToggle.setAttribute("aria-expanded", isActive ? "true" : "false");

        if (isActive) {
            closeNavMenuInContainer(controlsContainer);
            closeAccountMenuInContainer(controlsContainer);
            if (focusInput) {
                window.requestAnimationFrame(() => {
                    searchInput.focus();
                    searchInput.select();
                });
            }

            if (searchInput.value.trim().length >= SEARCH_MIN_QUERY_LENGTH) {
                scheduleSuggestionsFetch(searchInput.value);
            }
            return;
        }

        clearPendingSearch();
        hideSuggestions();

        if (focusToggle) {
            window.requestAnimationFrame(() => {
                searchToggle.focus();
            });
        }
    };

    setSearchState(false);

    searchToggle.addEventListener("click", (event) => {
        event.preventDefault();
        if (isSearchActive()) {
            setSearchState(false, { focusToggle: true });
            return;
        }

        setSearchState(true, { focusInput: true });
    });

    searchInput.addEventListener("input", () => {
        if (!isSearchActive()) {
            return;
        }

        scheduleSuggestionsFetch(searchInput.value);
    });

    searchInput.addEventListener("focus", () => {
        if (!isSearchActive()) {
            return;
        }

        if (searchInput.value.trim().length >= SEARCH_MIN_QUERY_LENGTH) {
            scheduleSuggestionsFetch(searchInput.value);
        }
    });

    suggestionsList.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        if (target.closest(".site-search-suggestion")) {
            setSearchState(false);
        }
    });

    searchForm.addEventListener("focusout", () => {
        window.setTimeout(() => {
            const activeElement = document.activeElement;
            if (activeElement instanceof Node && searchForm.contains(activeElement)) {
                return;
            }

            setSearchState(false);
        }, 0);
    });

    document.addEventListener("click", (event) => {
        if (!isSearchActive()) {
            return;
        }

        const target = event.target;
        if (target instanceof Node && controlsContainer.contains(target)) {
            return;
        }

        setSearchState(false);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || !isSearchActive()) {
            return;
        }

        event.preventDefault();
        setSearchState(false, { focusToggle: true });
    });

    document.addEventListener("enhancedload", () => {
        setSearchState(false);
    });

    searchForm.dataset.searchWired = "true";
}

function wireAccountMenu(accountToggle) {
    if (!(accountToggle instanceof HTMLButtonElement) || accountToggle.dataset.accountMenuWired === "true") {
        return;
    }

    const accountRoot = accountToggle.closest(ACCOUNT_MENU_ROOT_SELECTOR);
    const accountDropdown = accountRoot?.querySelector(ACCOUNT_DROPDOWN_SELECTOR);
    const navControls = accountRoot?.closest(".nav-controls");
    if (!(accountRoot instanceof HTMLElement) || !(accountDropdown instanceof HTMLElement)) {
        return;
    }

    const isOpen = () => accountRoot.classList.contains(OPEN_CLASS);

    const setMenuState = (open, options = {}) => {
        const { focusToggle = false } = options;
        accountRoot.classList.toggle(OPEN_CLASS, open);
        accountToggle.setAttribute("aria-expanded", open ? "true" : "false");

        if (open && navControls instanceof HTMLElement) {
            closeNavMenuInContainer(navControls);
            navControls.classList.remove(SEARCH_ACTIVE_CLASS);
        }

        if (!open && focusToggle) {
            window.requestAnimationFrame(() => {
                accountToggle.focus();
            });
        }
    };

    setMenuState(false);

    accountToggle.addEventListener("click", (event) => {
        event.preventDefault();
        setMenuState(!isOpen());
    });

    accountDropdown.querySelectorAll("a").forEach((link) => {
        link.addEventListener("click", () => {
            setMenuState(false);
        });
    });

    document.addEventListener("click", (event) => {
        if (!isOpen()) {
            return;
        }

        const target = event.target;
        if (target instanceof Node && accountRoot.contains(target)) {
            return;
        }

        setMenuState(false);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || !isOpen()) {
            return;
        }

        event.preventDefault();
        setMenuState(false, { focusToggle: true });
    });

    document.addEventListener("enhancedload", () => {
        setMenuState(false);
    });

    accountToggle.dataset.accountMenuWired = "true";
}

function initializeNavMenus() {
    document.querySelectorAll(NAV_TOGGLE_SELECTOR).forEach((toggleButton) => {
        wireNavToggle(toggleButton);
    });
}

function initializeHeaderSearch() {
    document.querySelectorAll(SEARCH_FORM_SELECTOR).forEach((searchForm) => {
        wireHeaderSearch(searchForm);
    });
}

function initializeAccountMenus() {
    document.querySelectorAll(ACCOUNT_TOGGLE_SELECTOR).forEach((accountToggle) => {
        wireAccountMenu(accountToggle);
    });
}

function initializeHeaderInteractions() {
    initializeNavMenus();
    initializeHeaderSearch();
    initializeAccountMenus();
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeHeaderInteractions, { once: true });
} else {
    initializeHeaderInteractions();
}

document.addEventListener("enhancedload", initializeHeaderInteractions);
