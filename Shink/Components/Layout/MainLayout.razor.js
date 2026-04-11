const NAV_TOGGLE_SELECTOR = "[data-nav-menu-toggle]";
const NAV_TOGGLE_LABEL_SELECTOR = "[data-nav-menu-toggle-label]";
const SEARCH_FORM_SELECTOR = "[data-search-form]";
const SEARCH_TOGGLE_SELECTOR = "[data-search-toggle]";
const SEARCH_INPUT_SELECTOR = "[data-search-input]";
const SEARCH_LOADER_SELECTOR = "[data-search-loader]";
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
let navMenuDelegatesStarted = false;
let accountMenuDelegatesStarted = false;

function findNavMenuParts(from) {
    const navToggle = from instanceof Element && from.matches(NAV_TOGGLE_SELECTOR)
        ? from
        : null;
    const controlsContainer = navToggle instanceof Element
        ? navToggle.closest(".nav-controls, .guest-controls")
        : from instanceof Element
            ? from.closest(".nav-controls, .guest-controls")
            : null;
    const resolvedNavToggle = navToggle instanceof HTMLButtonElement
        ? navToggle
        : controlsContainer instanceof Element
            ? controlsContainer.querySelector(NAV_TOGGLE_SELECTOR)
            : null;
    const navId = resolvedNavToggle instanceof HTMLElement
        ? resolvedNavToggle.getAttribute("aria-controls")
        : null;
    let navMenu = null;

    if (controlsContainer instanceof Element && typeof navId === "string" && navId.length > 0) {
        if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
            navMenu = controlsContainer.querySelector(`#${CSS.escape(navId)}`);
        } else {
            navMenu = controlsContainer.querySelector(`[id="${navId}"]`);
        }
    }

    if (!(navMenu instanceof HTMLElement) && typeof navId === "string" && navId.length > 0) {
        navMenu = document.getElementById(navId);
    }

    if (!(controlsContainer instanceof HTMLElement) ||
        !(resolvedNavToggle instanceof HTMLButtonElement) ||
        !(navMenu instanceof HTMLElement)) {
        return null;
    }

    return {
        controlsContainer,
        navToggle: resolvedNavToggle,
        navMenu,
        srLabel: resolvedNavToggle.querySelector(NAV_TOGGLE_LABEL_SELECTOR)
    };
}

function setNavMenuState(from, open) {
    const parts = findNavMenuParts(from);
    if (!parts) {
        return;
    }

    parts.navMenu.classList.toggle(OPEN_CLASS, open);
    parts.navToggle.setAttribute("aria-expanded", open ? "true" : "false");
    const label = open ? OPEN_LABEL : CLOSED_LABEL;
    parts.navToggle.setAttribute("aria-label", label);
    parts.navToggle.setAttribute("title", label);

    if (parts.srLabel instanceof HTMLElement) {
        parts.srLabel.textContent = label;
    }

    if (open) {
        closeAccountMenuInContainer(parts.controlsContainer);
        parts.controlsContainer.classList.remove(SEARCH_ACTIVE_CLASS);
    }
}

function closeAllNavMenus() {
    document.querySelectorAll(".nav-controls, .guest-controls").forEach((container) => {
        if (container instanceof HTMLElement) {
            setNavMenuState(container, false);
        }
    });
}

window.toggleSchinkNavMenu = (toggleButton) => {
    if (!(toggleButton instanceof HTMLButtonElement)) {
        return false;
    }

    const parts = findNavMenuParts(toggleButton);
    if (!parts) {
        return false;
    }

    const shouldOpen = !parts.navMenu.classList.contains(OPEN_CLASS);
    closeAllNavMenus();
    closeAllAccountMenus();
    setNavMenuState(parts.controlsContainer, shouldOpen);
    return false;
};

function findAccountMenuParts(from) {
    const accountRoot = from instanceof Element
        ? from.closest(ACCOUNT_MENU_ROOT_SELECTOR)
        : null;
    const accountToggle = accountRoot instanceof Element
        ? accountRoot.querySelector(ACCOUNT_TOGGLE_SELECTOR)
        : null;
    const accountDropdown = accountRoot instanceof Element
        ? accountRoot.querySelector(ACCOUNT_DROPDOWN_SELECTOR)
        : null;
    const navControls = accountRoot instanceof Element
        ? accountRoot.closest(".nav-controls")
        : null;

    if (!(accountRoot instanceof HTMLElement) ||
        !(accountToggle instanceof HTMLButtonElement) ||
        !(accountDropdown instanceof HTMLElement)) {
        return null;
    }

    return {
        accountRoot,
        accountToggle,
        accountDropdown,
        navControls: navControls instanceof HTMLElement ? navControls : null
    };
}

function setAccountMenuState(accountRoot, open, options = {}) {
    const parts = findAccountMenuParts(accountRoot);
    if (!parts) {
        return;
    }

    const { focusToggle = false } = options;
    parts.accountRoot.classList.toggle(OPEN_CLASS, open);
    parts.accountToggle.setAttribute("aria-expanded", open ? "true" : "false");

    if (open && parts.navControls instanceof HTMLElement) {
        closeNavMenuInContainer(parts.navControls);
        parts.navControls.classList.remove(SEARCH_ACTIVE_CLASS);
    }

    if (!open && focusToggle) {
        window.requestAnimationFrame(() => {
            parts.accountToggle.focus();
        });
    }
}

function closeAllAccountMenus(options = {}) {
    document.querySelectorAll(ACCOUNT_MENU_ROOT_SELECTOR).forEach((accountRoot) => {
        if (accountRoot instanceof HTMLElement && accountRoot.classList.contains(OPEN_CLASS)) {
            setAccountMenuState(accountRoot, false, options);
        }
    });
}

function closeNavMenuInContainer(container) {
    setNavMenuState(container, false);
}

function closeAccountMenuInContainer(container) {
    if (!(container instanceof Element)) {
        return;
    }

    const accountRoot = container.querySelector(ACCOUNT_MENU_ROOT_SELECTOR);
    const accountToggle = accountRoot instanceof Element
        ? accountRoot.querySelector(ACCOUNT_TOGGLE_SELECTOR)
        : null;
    if (!(accountRoot instanceof HTMLElement) || !(accountToggle instanceof HTMLElement)) {
        return;
    }

    accountRoot.classList.remove(OPEN_CLASS);
    accountToggle.setAttribute("aria-expanded", "false");
}

function initializeNavMenu(toggleButton) {
    if (!(toggleButton instanceof HTMLButtonElement)) {
        return;
    }

    if (toggleButton.dataset.navMenuWired === "true") {
        return;
    }

    const parts = findNavMenuParts(toggleButton);
    if (!parts) {
        return;
    }

    setNavMenuState(parts.controlsContainer, false);

    toggleButton.dataset.navMenuWired = "true";
}

function startNavMenuDelegates() {
    if (navMenuDelegatesStarted) {
        return;
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const clickedToggle = target.closest(NAV_TOGGLE_SELECTOR);
        if (clickedToggle instanceof HTMLButtonElement) {
            return;
        }

        const navLink = target.closest(".site-nav a");
        if (navLink instanceof HTMLAnchorElement) {
            const parts = findNavMenuParts(navLink);
            if (parts) {
                setNavMenuState(parts.controlsContainer, false);
            }
            return;
        }

        const clickedInsideControls = target.closest(".nav-controls, .guest-controls");
        if (clickedInsideControls instanceof HTMLElement) {
            return;
        }

        closeAllNavMenus();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        closeAllNavMenus();
    });

    document.addEventListener("enhancedload", () => {
        closeAllNavMenus();
    });

    navMenuDelegatesStarted = true;
}

function wireHeaderSearch(searchForm) {
    if (!(searchForm instanceof HTMLFormElement) || searchForm.dataset.searchWired === "true") {
        return;
    }

    const searchToggle = searchForm.querySelector(SEARCH_TOGGLE_SELECTOR);
    const searchInput = searchForm.querySelector(SEARCH_INPUT_SELECTOR);
    const searchLoader = searchForm.querySelector(SEARCH_LOADER_SELECTOR);
    const suggestionsPanel = searchForm.querySelector(SEARCH_SUGGESTIONS_SELECTOR);
    const suggestionsList = searchForm.querySelector(SEARCH_SUGGESTIONS_LIST_SELECTOR);
    const controlsContainer = searchForm.closest(".nav-controls, .guest-controls");
    if (!(searchToggle instanceof HTMLButtonElement) ||
        !(searchInput instanceof HTMLInputElement) ||
        !(searchLoader instanceof HTMLElement) ||
        !(suggestionsPanel instanceof HTMLElement) ||
        !(suggestionsList instanceof HTMLElement) ||
        !(controlsContainer instanceof HTMLElement)) {
        return;
    }

    let searchDebounceHandle = null;
    let searchRequestVersion = 0;
    let activeAbortController = null;

    const isSearchActive = () => controlsContainer.classList.contains(SEARCH_ACTIVE_CLASS);

    const setLoadingState = (isLoading) => {
        searchForm.classList.toggle("is-search-loading", isLoading);
        searchForm.setAttribute("aria-busy", isLoading ? "true" : "false");
        searchLoader.hidden = !isLoading;
    };

    const replaceChildrenCompat = (parent, child) => {
        while (parent.firstChild) {
            parent.removeChild(parent.firstChild);
        }

        if (child instanceof Node) {
            parent.appendChild(child);
        }
    };

    const hideSuggestions = () => {
        replaceChildrenCompat(suggestionsList);
        suggestionsPanel.hidden = true;
    };

    const showEmptyState = (message) => {
        const empty = document.createElement("p");
        empty.className = "site-search-suggestion-empty";
        empty.textContent = message;
        replaceChildrenCompat(suggestionsList, empty);
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

        replaceChildrenCompat(suggestionsList, fragment);
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

        setLoadingState(false);
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

            const results = payload && Array.isArray(payload.results) ? payload.results : [];
            renderSuggestions(results);
        } catch (error) {
            if (error instanceof DOMException && error.name === "AbortError") {
                return;
            }

            if (requestVersion === searchRequestVersion) {
                hideSuggestions();
            }
        } finally {
            if (requestVersion === searchRequestVersion) {
                setLoadingState(false);
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

        setLoadingState(true);
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

    const parts = findAccountMenuParts(accountToggle);
    if (!parts) {
        return;
    }

    setAccountMenuState(parts.accountRoot, false);
    accountToggle.dataset.accountMenuWired = "true";
}

function startAccountMenuDelegates() {
    if (accountMenuDelegatesStarted) {
        return;
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const toggle = target.closest(ACCOUNT_TOGGLE_SELECTOR);
        if (toggle instanceof HTMLButtonElement) {
            event.preventDefault();
            const parts = findAccountMenuParts(toggle);
            if (!parts) {
                return;
            }

            const shouldOpen = !parts.accountRoot.classList.contains(OPEN_CLASS);
            closeAllAccountMenus();
            setAccountMenuState(parts.accountRoot, shouldOpen);
            return;
        }

        const clickedInsideMenu = target.closest(ACCOUNT_MENU_ROOT_SELECTOR);
        if (clickedInsideMenu instanceof HTMLElement) {
            const clickedLink = target.closest(`${ACCOUNT_DROPDOWN_SELECTOR} a`);
            if (clickedLink instanceof HTMLAnchorElement) {
                setAccountMenuState(clickedInsideMenu, false);
            }
            return;
        }

        closeAllAccountMenus();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        const openRoot = document.querySelector(`${ACCOUNT_MENU_ROOT_SELECTOR}.${OPEN_CLASS}`);
        if (!(openRoot instanceof HTMLElement)) {
            return;
        }

        event.preventDefault();
        setAccountMenuState(openRoot, false, { focusToggle: true });
    });

    document.addEventListener("enhancedload", () => {
        closeAllAccountMenus();
    });

    accountMenuDelegatesStarted = true;
}

function initializeNavMenus() {
    document.querySelectorAll(NAV_TOGGLE_SELECTOR).forEach((toggleButton) => {
        initializeNavMenu(toggleButton);
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
    startNavMenuDelegates();
    startAccountMenuDelegates();
}

let headerInitFrameHandle = null;
let headerObserverStarted = false;

function scheduleHeaderInteractionsInitialization() {
    if (headerInitFrameHandle !== null) {
        return;
    }

    headerInitFrameHandle = window.requestAnimationFrame(() => {
        headerInitFrameHandle = null;
        initializeHeaderInteractions();
    });
}

function startHeaderObserver() {
    if (headerObserverStarted || !(document.body instanceof HTMLElement) || typeof MutationObserver === "undefined") {
        return;
    }

    const observer = new MutationObserver(() => {
        scheduleHeaderInteractionsInitialization();
    });

    observer.observe(document.body, {
        childList: true,
        subtree: true
    });

    headerObserverStarted = true;
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => {
        initializeHeaderInteractions();
        startHeaderObserver();
    }, { once: true });
} else {
    initializeHeaderInteractions();
    startHeaderObserver();
}

document.addEventListener("enhancedload", initializeHeaderInteractions);
window.addEventListener("pageshow", scheduleHeaderInteractionsInitialization);
window.addEventListener("popstate", scheduleHeaderInteractionsInitialization);
