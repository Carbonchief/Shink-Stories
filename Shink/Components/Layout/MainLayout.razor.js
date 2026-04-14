const NAV_TOGGLE_SELECTOR = "[data-nav-menu-toggle]";
const NAV_TOGGLE_LABEL_SELECTOR = "[data-nav-menu-toggle-label]";
const SEARCH_FORM_SELECTOR = "[data-search-form]";
const SEARCH_TOGGLE_SELECTOR = "[data-search-toggle]";
const SEARCH_INPUT_SELECTOR = "[data-search-input]";
const SEARCH_LOADER_SELECTOR = "[data-search-loader]";
const SEARCH_SUGGESTIONS_SELECTOR = "[data-search-suggestions]";
const SEARCH_SUGGESTIONS_LIST_SELECTOR = "[data-search-suggestions-list]";
const NOTIFICATION_CENTER_ROOT_SELECTOR = "[data-notification-center-root]";
const NOTIFICATION_TOGGLE_SELECTOR = "[data-notification-toggle]";
const NOTIFICATION_PANEL_SELECTOR = "[data-notification-panel]";
const NOTIFICATION_LIST_SELECTOR = "[data-notification-list]";
const NOTIFICATION_LOADING_SELECTOR = "[data-notification-loading]";
const NOTIFICATION_COUNT_SELECTOR = "[data-notification-count]";
const NOTIFICATION_PANEL_COUNT_SELECTOR = "[data-notification-panel-count]";
const NOTIFICATION_CLEAR_SELECTOR = "[data-notification-clear]";
const NOTIFICATION_ITEM_CLEAR_SELECTOR = "[data-notification-item-clear]";
const NOTIFICATION_FOOTER_SELECTOR = "[data-notification-footer]";
const NOTIFICATION_LOAD_MORE_SELECTOR = "[data-notification-load-more]";
const ACCOUNT_MENU_ROOT_SELECTOR = "[data-account-menu-root]";
const ACCOUNT_TOGGLE_SELECTOR = "[data-account-toggle]";
const ACCOUNT_DROPDOWN_SELECTOR = "[data-account-dropdown]";
const OPEN_CLASS = "is-open";
const SEARCH_ACTIVE_CLASS = "is-search-active";
const SEARCH_SUGGEST_ENDPOINT = "/api/search/suggest";
const NOTIFICATION_ENDPOINT = "/api/notifications";
const NOTIFICATION_READ_ALL_ENDPOINT = "/api/notifications/read-all";
const NOTIFICATION_CLEAR_ENDPOINT = "/api/notifications/clear";
const NOTIFICATION_REFRESH_EVENT = "schink:notifications-refresh";
const NOTIFICATION_REMOVE_ANIMATION_MS = 180;
const NOTIFICATION_PAGE_SIZE = 10;
const SEARCH_MIN_QUERY_LENGTH = 2;
const SEARCH_DEBOUNCE_MS = 160;
const SEARCH_MAX_RESULTS = 8;
const OPEN_LABEL = "Maak navigasie toe";
const CLOSED_LABEL = "Maak navigasie oop";
let navMenuDelegatesStarted = false;
let notificationCenterDelegatesStarted = false;
let accountMenuDelegatesStarted = false;
const notificationCenterState = new WeakMap();
const notificationDateFormatter = typeof Intl !== "undefined"
    ? new Intl.DateTimeFormat("af-ZA", { day: "numeric", month: "short" })
    : null;

function buildNotificationClearItemEndpoint(notificationId) {
    if (typeof notificationId !== "string" || notificationId.length === 0) {
        return null;
    }

    return `/api/notifications/${encodeURIComponent(notificationId)}/clear`;
}

function replaceNodeChildren(parent, child) {
    while (parent.firstChild) {
        parent.removeChild(parent.firstChild);
    }

    if (child instanceof Node) {
        parent.appendChild(child);
    }
}

function delay(milliseconds) {
    return new Promise((resolve) => {
        window.setTimeout(resolve, milliseconds);
    });
}

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
        closeNotificationCenterInContainer(parts.controlsContainer);
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
    closeAllNotificationCenters();
    closeAllAccountMenus();
    setNavMenuState(parts.controlsContainer, shouldOpen);
    return false;
};

function getNotificationCenterState(centerRoot) {
    let state = notificationCenterState.get(centerRoot);
    if (!state) {
        state = {
            isLoaded: false,
            isLoading: false,
            isMarkingRead: false,
            isClearing: false,
            isLoadingMore: false,
            clearingNotificationIds: new Set(),
            removingNotificationIds: new Set(),
            notifications: [],
            unreadCount: 0,
            hasMore: false,
            hasHistory: false,
            nextBefore: null,
            isShowingHistory: false
        };
        notificationCenterState.set(centerRoot, state);
    }

    return state;
}

function findNotificationCenterParts(from) {
    const centerRoot = from instanceof Element
        ? from.closest(NOTIFICATION_CENTER_ROOT_SELECTOR)
        : null;
    const notificationToggle = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_TOGGLE_SELECTOR)
        : null;
    const notificationPanel = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_PANEL_SELECTOR)
        : null;
    const notificationList = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_LIST_SELECTOR)
        : null;
    const notificationLoading = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_LOADING_SELECTOR)
        : null;
    const notificationCount = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_COUNT_SELECTOR)
        : null;
    const notificationPanelCount = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_PANEL_COUNT_SELECTOR)
        : null;
    const notificationClear = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_CLEAR_SELECTOR)
        : null;
    const notificationFooter = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_FOOTER_SELECTOR)
        : null;
    const notificationLoadMore = centerRoot instanceof Element
        ? centerRoot.querySelector(NOTIFICATION_LOAD_MORE_SELECTOR)
        : null;
    const navControls = centerRoot instanceof Element
        ? centerRoot.closest(".nav-controls")
        : null;

    if (!(centerRoot instanceof HTMLElement) ||
        !(notificationToggle instanceof HTMLButtonElement) ||
        !(notificationPanel instanceof HTMLElement) ||
        !(notificationList instanceof HTMLElement) ||
        !(notificationLoading instanceof HTMLElement)) {
        return null;
    }

    return {
        centerRoot,
        notificationToggle,
        notificationPanel,
        notificationList,
        notificationLoading,
        notificationCount: notificationCount instanceof HTMLElement ? notificationCount : null,
        notificationPanelCount: notificationPanelCount instanceof HTMLElement ? notificationPanelCount : null,
        notificationClear: notificationClear instanceof HTMLButtonElement ? notificationClear : null,
        notificationFooter: notificationFooter instanceof HTMLElement ? notificationFooter : null,
        notificationLoadMore: notificationLoadMore instanceof HTMLButtonElement ? notificationLoadMore : null,
        navControls: navControls instanceof HTMLElement ? navControls : null
    };
}

function formatNotificationCount(count) {
    if (!Number.isFinite(count) || count <= 0) {
        return "";
    }

    return count > 99 ? "99+" : String(count);
}

function setNotificationCount(parts, count) {
    const formattedCount = formatNotificationCount(count);
    const hasCount = formattedCount.length > 0;

    if (parts.notificationCount instanceof HTMLElement) {
        parts.notificationCount.hidden = !hasCount;
        parts.notificationCount.textContent = formattedCount;
    }

    if (parts.notificationPanelCount instanceof HTMLElement) {
        parts.notificationPanelCount.hidden = !hasCount;
        parts.notificationPanelCount.textContent = hasCount
            ? `${count} ${count === 1 ? "ongelees" : "ongelees"}`
            : "";
    }

    const label = hasCount
        ? `Kennisgewings (${count} ongelees)`
        : "Kennisgewings";
    parts.notificationToggle.setAttribute("aria-label", label);
    parts.notificationToggle.setAttribute("title", label);
}

function syncNotificationClearButton(parts, state) {
    if (!(parts.notificationClear instanceof HTMLButtonElement)) {
        return;
    }

    const hasNotifications = Array.isArray(state.notifications) && state.notifications.length > 0;
    const hasItemClearInFlight = state.clearingNotificationIds instanceof Set && state.clearingNotificationIds.size > 0;
    const hasItemRemovalInFlight = state.removingNotificationIds instanceof Set && state.removingNotificationIds.size > 0;
    parts.notificationClear.hidden = !hasNotifications;
    parts.notificationClear.disabled = state.isClearing || state.isLoading || state.isLoadingMore || hasItemClearInFlight || hasItemRemovalInFlight || !hasNotifications;
    parts.notificationClear.textContent = state.isClearing ? "Maak skoon..." : "Maak skoon";
}

function syncNotificationLoadMoreButton(parts, state) {
    if (!(parts.notificationFooter instanceof HTMLElement) ||
        !(parts.notificationLoadMore instanceof HTMLButtonElement)) {
        return;
    }

    const shouldShow = state.hasMore || state.hasHistory;
    parts.notificationFooter.hidden = !shouldShow;
    parts.notificationLoadMore.hidden = !shouldShow;
    parts.notificationLoadMore.disabled = state.isLoading || state.isLoadingMore || state.isClearing;
    parts.notificationLoadMore.textContent = state.isLoadingMore
        ? "Laai vorige kennisgewings..."
        : "Wys vorige kennisgewings";
}

function formatNotificationDate(createdAt) {
    if (typeof createdAt !== "string" || createdAt.length === 0) {
        return "";
    }

    const parsedDate = new Date(createdAt);
    if (!Number.isFinite(parsedDate.getTime())) {
        return "";
    }

    if (notificationDateFormatter) {
        return notificationDateFormatter.format(parsedDate);
    }

    return parsedDate.toLocaleDateString();
}

function normalizeNotificationType(notificationType) {
    return typeof notificationType === "string" && notificationType.length > 0
        ? notificationType.trim().toLowerCase()
        : "";
}

function buildNotificationTypeMeta(notificationType) {
    switch (normalizeNotificationType(notificationType)) {
    case "character_unlock":
        return {
            label: "Karakter",
            badgeClass: "is-character",
            icon: "fa-user-astronaut"
        };
    case "story_published":
        return {
            label: "Nuwe storie",
            badgeClass: "is-story",
            icon: "fa-book-open"
        };
    default:
        return {
            label: "Kennisgewing",
            badgeClass: "is-general",
            icon: "fa-bell"
        };
    }
}

function renderNotificationItems(parts, notifications, fallbackMessage) {
    const safeNotifications = Array.isArray(notifications) ? notifications : [];
    const state = getNotificationCenterState(parts.centerRoot);
    if (safeNotifications.length === 0) {
        const empty = document.createElement("p");
        empty.className = "notification-item-empty";
        empty.textContent = fallbackMessage || "Geen kennisgewings nog nie.";
        replaceNodeChildren(parts.notificationList, empty);
        return;
    }

    const fragment = document.createDocumentFragment();
    safeNotifications.forEach((notification) => {
        const notificationId = notification && typeof notification.id === "string"
            ? notification.id
            : "";
        const href = notification && typeof notification.href === "string" && notification.href.length > 0
            ? notification.href
            : null;
        const shell = document.createElement("div");
        const isRemovingItem = state.removingNotificationIds instanceof Set
            ? state.removingNotificationIds.has(notificationId)
            : false;
        shell.className = `notification-item-shell${isRemovingItem ? " is-removing" : ""}`;

        const item = document.createElement(href ? "a" : "div");
        const isRead = Boolean(notification && notification.isRead);
        const typeMeta = buildNotificationTypeMeta(notification && notification.type);
        item.className = `notification-item ${typeMeta.badgeClass}${isRead ? "" : " is-unread"}`;
        if (href && item instanceof HTMLAnchorElement) {
            item.href = href;
        }

        const imageWrap = document.createElement("span");
        imageWrap.className = "notification-item-image-wrap";

        const image = document.createElement("img");
        image.className = "notification-item-image";
        image.alt = notification && typeof notification.imageAlt === "string" && notification.imageAlt.length > 0
            ? notification.imageAlt
            : "";
        image.loading = "lazy";
        image.decoding = "async";
        image.src = notification && typeof notification.imagePath === "string" && notification.imagePath.length > 0
            ? notification.imagePath
            : "/branding/schink-logo-green.png";
        imageWrap.append(image);

        const imageBadge = document.createElement("span");
        imageBadge.className = `notification-item-image-badge ${typeMeta.badgeClass}`;
        imageBadge.setAttribute("aria-hidden", "true");
        imageBadge.innerHTML = `<i class="fa-solid ${typeMeta.icon}" aria-hidden="true"></i>`;
        imageWrap.append(imageBadge);
        item.append(imageWrap);

        const copy = document.createElement("span");
        copy.className = "notification-item-copy";

        const metaRow = document.createElement("span");
        metaRow.className = "notification-item-meta";

        const typeBadge = document.createElement("span");
        typeBadge.className = `notification-item-type ${typeMeta.badgeClass}`;
        typeBadge.textContent = typeMeta.label;
        metaRow.append(typeBadge);

        const dateText = formatNotificationDate(notification && notification.createdAt);
        if (dateText.length > 0) {
            const date = document.createElement("p");
            date.className = "notification-item-date";
            date.textContent = dateText;
            metaRow.append(date);
        }

        copy.append(metaRow);

        const title = document.createElement("p");
        title.className = "notification-item-title";
        title.textContent = notification && typeof notification.title === "string" && notification.title.length > 0
            ? notification.title
            : "Kennisgewing";
        copy.append(title);

        if (notification && typeof notification.body === "string" && notification.body.length > 0) {
            const body = document.createElement("p");
            body.className = "notification-item-body";
            body.textContent = notification.body;
            copy.append(body);
        }

        item.append(copy);

        const titleText = notification && typeof notification.title === "string" && notification.title.length > 0
            ? notification.title
            : "Kennisgewing";
        const clearButton = document.createElement("button");
        const isClearingItem = state.clearingNotificationIds instanceof Set
            ? state.clearingNotificationIds.has(notificationId)
            : false;
        clearButton.type = "button";
        clearButton.className = "notification-item-clear";
        clearButton.dataset.notificationItemClear = "true";
        clearButton.dataset.notificationId = notificationId;
        clearButton.disabled = state.isClearing || state.isLoading || state.isLoadingMore || isClearingItem || isRemovingItem || notificationId.length === 0;
        clearButton.setAttribute("aria-label", `Verwyder kennisgewing: ${titleText}`);
        clearButton.title = "Verwyder kennisgewing";
        clearButton.innerHTML = `<i class="fa-solid fa-xmark" aria-hidden="true"></i>`;

        shell.append(item);
        shell.append(clearButton);
        fragment.append(shell);
    });

    replaceNodeChildren(parts.notificationList, fragment);
}

function buildNotificationRequestUrl(options = {}) {
    const { limit = NOTIFICATION_PAGE_SIZE, before = null, history = false } = options;
    const url = new URL(NOTIFICATION_ENDPOINT, window.location.origin);
    url.searchParams.set("limit", String(limit));

    if (typeof before === "string" && before.length > 0) {
        url.searchParams.set("before", before);
    }

    if (history) {
        url.searchParams.set("history", "true");
    }

    return `${url.pathname}${url.search}`;
}

function getOldestNotificationCreatedAt(notifications) {
    if (!Array.isArray(notifications) || notifications.length === 0) {
        return null;
    }

    const oldestNotification = notifications[notifications.length - 1];
    return oldestNotification && typeof oldestNotification.createdAt === "string" && oldestNotification.createdAt.length > 0
        ? oldestNotification.createdAt
        : null;
}

async function loadNotificationCenter(centerRoot, options = {}) {
    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const { force = false } = options;
    const state = getNotificationCenterState(parts.centerRoot);
    if (state.isLoading || (state.isLoaded && !force)) {
        return;
    }

    state.isLoading = true;
    parts.notificationLoading.hidden = false;
    syncNotificationClearButton(parts, state);

    try {
        const response = await fetch(buildNotificationRequestUrl(), {
            method: "GET",
            credentials: "same-origin",
            headers: {
                Accept: "application/json"
            }
        });

        if (!response.ok) {
            state.hasMore = false;
            state.nextBefore = null;
            setNotificationCount(parts, 0);
            renderNotificationItems(
                parts,
                [],
                response.status === 401
                    ? "Teken in om kennisgewings te sien."
                    : "Ons kon nie nou die kennisgewings laai nie.");
            state.isLoaded = false;
            syncNotificationClearButton(parts, state);
            syncNotificationLoadMoreButton(parts, state);
            return;
        }

        const payload = await response.json();
        const notifications = payload && Array.isArray(payload.notifications)
            ? payload.notifications
            : [];
        const unreadCount = payload && Number.isFinite(payload.unreadCount)
            ? payload.unreadCount
            : notifications.filter((notification) => !notification.isRead).length;

        state.notifications = notifications;
        state.unreadCount = unreadCount;
        state.hasMore = Boolean(payload && payload.hasMore);
        state.hasHistory = Boolean(payload && payload.hasHistory);
        state.nextBefore = getOldestNotificationCreatedAt(notifications);
        state.isShowingHistory = false;
        state.clearingNotificationIds = new Set();
        state.removingNotificationIds = new Set();
        setNotificationCount(parts, unreadCount);
        renderNotificationItems(parts, notifications);
        state.isLoaded = true;
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);

        if (parts.centerRoot.classList.contains(OPEN_CLASS) && unreadCount > 0) {
            void markNotificationCenterRead(parts.centerRoot);
        }
    } catch {
        state.notifications = [];
        state.unreadCount = 0;
        state.hasMore = false;
        state.hasHistory = false;
        state.nextBefore = null;
        state.isShowingHistory = false;
        state.clearingNotificationIds = new Set();
        state.removingNotificationIds = new Set();
        setNotificationCount(parts, 0);
        renderNotificationItems(parts, [], "Ons kon nie nou die kennisgewings laai nie.");
        state.isLoaded = false;
    } finally {
        state.isLoading = false;
        parts.notificationLoading.hidden = true;
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    }
}

async function loadMoreNotifications(centerRoot) {
    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const state = getNotificationCenterState(parts.centerRoot);
    const openHistoryFromEmpty = (!Array.isArray(state.notifications) || state.notifications.length === 0) && state.hasHistory;
    if (state.isLoading || state.isLoadingMore || state.isClearing || !state.isLoaded || (!state.hasMore && !state.hasHistory)) {
        return;
    }

    const shouldLoadHistory = state.isShowingHistory || (!state.hasMore && state.hasHistory);
    const before = typeof state.nextBefore === "string" && state.nextBefore.length > 0
        ? state.nextBefore
        : getOldestNotificationCreatedAt(state.notifications);
    if (!shouldLoadHistory && (typeof before !== "string" || before.length === 0)) {
        state.hasMore = false;
        syncNotificationLoadMoreButton(parts, state);
        return;
    }

    state.isLoadingMore = true;
    syncNotificationClearButton(parts, state);
    syncNotificationLoadMoreButton(parts, state);

    try {
        const response = await fetch(buildNotificationRequestUrl({
            before,
            history: shouldLoadHistory
        }), {
            method: "GET",
            credentials: "same-origin",
            headers: {
                Accept: "application/json"
            }
        });

        if (!response.ok) {
            return;
        }

        const payload = await response.json();
        const notifications = payload && Array.isArray(payload.notifications)
            ? payload.notifications
            : [];

        if (notifications.length === 0) {
            state.hasMore = false;
            state.hasHistory = false;
            return;
        }

        state.notifications = openHistoryFromEmpty
            ? notifications
            : [...state.notifications, ...notifications];
        state.hasMore = Boolean(payload && payload.hasMore);
        state.hasHistory = Boolean(payload && payload.hasHistory);
        state.nextBefore = getOldestNotificationCreatedAt(notifications) || state.nextBefore;
        state.isShowingHistory = shouldLoadHistory;
        renderNotificationItems(parts, state.notifications);
    } catch {
        // Ignore pagination failures to keep the current list usable.
    } finally {
        state.isLoadingMore = false;
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    }
}

async function markNotificationCenterRead(centerRoot) {
    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const state = getNotificationCenterState(parts.centerRoot);
    if (state.isMarkingRead || state.unreadCount <= 0) {
        return;
    }

    state.isMarkingRead = true;
    syncNotificationClearButton(parts, state);

    try {
        const response = await fetch(NOTIFICATION_READ_ALL_ENDPOINT, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json"
            },
            body: JSON.stringify({})
        });

        if (!response.ok) {
            return;
        }

        state.notifications = Array.isArray(state.notifications)
            ? state.notifications.map((notification) => ({
                ...notification,
                isRead: true
            }))
            : [];
        state.unreadCount = 0;
        setNotificationCount(parts, 0);
        renderNotificationItems(parts, state.notifications);
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    } catch {
        // Ignore mark-read failures to keep the panel usable.
    } finally {
        state.isMarkingRead = false;
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    }
}

async function clearNotificationCenter(centerRoot) {
    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const state = getNotificationCenterState(parts.centerRoot);
    if (state.isClearing ||
        (state.clearingNotificationIds instanceof Set && state.clearingNotificationIds.size > 0) ||
        (state.removingNotificationIds instanceof Set && state.removingNotificationIds.size > 0) ||
        !Array.isArray(state.notifications) ||
        state.notifications.length === 0) {
        return;
    }

    state.isClearing = true;
    renderNotificationItems(parts, state.notifications);
    syncNotificationClearButton(parts, state);

    try {
        const response = await fetch(NOTIFICATION_CLEAR_ENDPOINT, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json"
            },
            body: JSON.stringify({})
        });

        if (!response.ok) {
            return;
        }

        state.removingNotificationIds = new Set(
            state.notifications
                .map((notification) => notification && typeof notification.id === "string" ? notification.id : "")
                .filter((notificationId) => notificationId.length > 0));
        state.unreadCount = 0;
        setNotificationCount(parts, 0);
        renderNotificationItems(parts, state.notifications);
        syncNotificationClearButton(parts, state);
        await delay(NOTIFICATION_REMOVE_ANIMATION_MS);

        state.notifications = [];
        state.hasMore = false;
        state.hasHistory = false;
        state.nextBefore = null;
        state.isShowingHistory = false;
        state.clearingNotificationIds = new Set();
        state.removingNotificationIds = new Set();
        renderNotificationItems(parts, [], "Geen kennisgewings nog nie.");
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    } catch {
        // Ignore clear failures to keep the panel usable.
    } finally {
        state.isClearing = false;
        state.clearingNotificationIds = new Set();
        state.removingNotificationIds = new Set();
        renderNotificationItems(
            parts,
            state.notifications,
            state.notifications.length === 0 ? "Geen kennisgewings nog nie." : undefined);
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    }
}

async function clearSingleNotification(centerRoot, notificationId) {
    if (typeof notificationId !== "string" || notificationId.length === 0) {
        return;
    }

    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const state = getNotificationCenterState(parts.centerRoot);
    if (state.isClearing ||
        !(state.clearingNotificationIds instanceof Set) ||
        state.clearingNotificationIds.has(notificationId) ||
        (state.removingNotificationIds instanceof Set && state.removingNotificationIds.has(notificationId)) ||
        !Array.isArray(state.notifications) ||
        !state.notifications.some((notification) => notification && notification.id === notificationId)) {
        return;
    }

    const endpoint = buildNotificationClearItemEndpoint(notificationId);
    if (!endpoint) {
        return;
    }

    const notificationToClear = state.notifications.find((notification) => notification && notification.id === notificationId);
    const isAlreadyCleared = Boolean(notificationToClear && notificationToClear.isCleared);

    state.clearingNotificationIds.add(notificationId);
    renderNotificationItems(parts, state.notifications);
    syncNotificationClearButton(parts, state);

    try {
        if (!isAlreadyCleared) {
            const response = await fetch(endpoint, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    Accept: "application/json"
                },
                body: JSON.stringify({})
            });

            if (!response.ok) {
                return;
            }
        }

        state.removingNotificationIds.add(notificationId);
        renderNotificationItems(parts, state.notifications);
        syncNotificationClearButton(parts, state);
        await delay(NOTIFICATION_REMOVE_ANIMATION_MS);

        state.notifications = state.notifications.filter((notification) => notification && notification.id !== notificationId);
        state.unreadCount = state.notifications.filter((notification) => notification && !notification.isRead).length;
        setNotificationCount(parts, state.unreadCount);
        renderNotificationItems(
            parts,
            state.notifications,
            state.notifications.length === 0 ? "Geen kennisgewings nog nie." : undefined);
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    } catch {
        // Ignore single-clear failures to keep the panel usable.
    } finally {
        state.clearingNotificationIds.delete(notificationId);
        state.removingNotificationIds.delete(notificationId);
        renderNotificationItems(
            parts,
            state.notifications,
            state.notifications.length === 0 ? "Geen kennisgewings nog nie." : undefined);
        syncNotificationClearButton(parts, state);
        syncNotificationLoadMoreButton(parts, state);
    }
}

function setNotificationCenterState(centerRoot, open, options = {}) {
    const parts = findNotificationCenterParts(centerRoot);
    if (!parts) {
        return;
    }

    const { focusToggle = false } = options;
    parts.centerRoot.classList.toggle(OPEN_CLASS, open);
    parts.notificationToggle.setAttribute("aria-expanded", open ? "true" : "false");
    parts.notificationPanel.hidden = !open;

    if (open) {
        if (parts.navControls instanceof HTMLElement) {
            closeNavMenuInContainer(parts.navControls);
            parts.navControls.classList.remove(SEARCH_ACTIVE_CLASS);
            closeAccountMenuInContainer(parts.navControls);
        }

        const state = getNotificationCenterState(parts.centerRoot);
        if (state.isLoaded) {
            renderNotificationItems(parts, state.notifications);
            setNotificationCount(parts, state.unreadCount);
            syncNotificationClearButton(parts, state);
            syncNotificationLoadMoreButton(parts, state);
            if (state.unreadCount > 0) {
                void markNotificationCenterRead(parts.centerRoot);
            }
        } else {
            void loadNotificationCenter(parts.centerRoot);
        }
    }

    if (!open && focusToggle) {
        window.requestAnimationFrame(() => {
            parts.notificationToggle.focus();
        });
    }
}

function closeAllNotificationCenters(options = {}) {
    document.querySelectorAll(NOTIFICATION_CENTER_ROOT_SELECTOR).forEach((centerRoot) => {
        if (centerRoot instanceof HTMLElement && centerRoot.classList.contains(OPEN_CLASS)) {
            setNotificationCenterState(centerRoot, false, options);
        }
    });
}

function closeNotificationCenterInContainer(container) {
    if (!(container instanceof Element)) {
        return;
    }

    const centerRoot = container.querySelector(NOTIFICATION_CENTER_ROOT_SELECTOR);
    if (!(centerRoot instanceof HTMLElement)) {
        return;
    }

    setNotificationCenterState(centerRoot, false);
}

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
        closeNotificationCenterInContainer(parts.navControls);
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
            closeNotificationCenterInContainer(controlsContainer);
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

function wireNotificationCenter(notificationToggle) {
    if (!(notificationToggle instanceof HTMLButtonElement) ||
        notificationToggle.dataset.notificationCenterWired === "true") {
        return;
    }

    const parts = findNotificationCenterParts(notificationToggle);
    if (!parts) {
        return;
    }

    setNotificationCenterState(parts.centerRoot, false);

    if (parts.notificationPanel.dataset.notificationWheelLockWired !== "true") {
        parts.notificationPanel.addEventListener("wheel", (event) => {
            if (!(event.currentTarget instanceof HTMLElement) ||
                !(event.target instanceof Node) ||
                !event.currentTarget.contains(event.target)) {
                return;
            }

            if (event.deltaY === 0) {
                return;
            }

            event.preventDefault();

            if (!(parts.notificationList instanceof HTMLElement)) {
                return;
            }

            parts.notificationList.scrollTop += event.deltaY;
        }, { passive: false });

        parts.notificationPanel.dataset.notificationWheelLockWired = "true";
    }

    notificationToggle.dataset.notificationCenterWired = "true";
    void loadNotificationCenter(parts.centerRoot, { force: true });
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

function startNotificationCenterDelegates() {
    if (notificationCenterDelegatesStarted) {
        return;
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const toggle = target.closest(NOTIFICATION_TOGGLE_SELECTOR);
        if (toggle instanceof HTMLButtonElement) {
            event.preventDefault();
            const parts = findNotificationCenterParts(toggle);
            if (!parts) {
                return;
            }

            const shouldOpen = !parts.centerRoot.classList.contains(OPEN_CLASS);
            closeAllNavMenus();
            closeAllAccountMenus();
            closeAllNotificationCenters();
            setNotificationCenterState(parts.centerRoot, shouldOpen);
            return;
        }

        const clickedInsideCenter = target.closest(NOTIFICATION_CENTER_ROOT_SELECTOR);
        if (clickedInsideCenter instanceof HTMLElement) {
            const clearItemButton = target.closest(NOTIFICATION_ITEM_CLEAR_SELECTOR);
            if (clearItemButton instanceof HTMLButtonElement) {
                event.preventDefault();
                event.stopPropagation();
                const notificationId = clearItemButton.dataset.notificationId;
                void clearSingleNotification(clickedInsideCenter, notificationId);
                return;
            }

            const clearButton = target.closest(NOTIFICATION_CLEAR_SELECTOR);
            if (clearButton instanceof HTMLButtonElement) {
                event.preventDefault();
                void clearNotificationCenter(clickedInsideCenter);
                return;
            }

            const loadMoreButton = target.closest(NOTIFICATION_LOAD_MORE_SELECTOR);
            if (loadMoreButton instanceof HTMLButtonElement) {
                event.preventDefault();
                void loadMoreNotifications(clickedInsideCenter);
                return;
            }

            const clickedLink = target.closest(`${NOTIFICATION_PANEL_SELECTOR} a`);
            if (clickedLink instanceof HTMLAnchorElement) {
                setNotificationCenterState(clickedInsideCenter, false);
            }
            return;
        }

        closeAllNotificationCenters();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        const openRoot = document.querySelector(`${NOTIFICATION_CENTER_ROOT_SELECTOR}.${OPEN_CLASS}`);
        if (!(openRoot instanceof HTMLElement)) {
            return;
        }

        event.preventDefault();
        setNotificationCenterState(openRoot, false, { focusToggle: true });
    });

    document.addEventListener("enhancedload", () => {
        closeAllNotificationCenters();
        document.querySelectorAll(NOTIFICATION_CENTER_ROOT_SELECTOR).forEach((centerRoot) => {
            if (centerRoot instanceof HTMLElement) {
                const state = getNotificationCenterState(centerRoot);
                state.isLoaded = false;
                state.notifications = [];
                state.unreadCount = 0;
                state.isClearing = false;
                state.isLoadingMore = false;
                state.hasMore = false;
                state.hasHistory = false;
                state.nextBefore = null;
                state.isShowingHistory = false;
                state.clearingNotificationIds = new Set();
                state.removingNotificationIds = new Set();
                void loadNotificationCenter(centerRoot, { force: true });
            }
        });
    });

    window.addEventListener(NOTIFICATION_REFRESH_EVENT, () => {
        document.querySelectorAll(NOTIFICATION_CENTER_ROOT_SELECTOR).forEach((centerRoot) => {
            if (centerRoot instanceof HTMLElement) {
                const state = getNotificationCenterState(centerRoot);
                state.isLoaded = false;
                state.notifications = [];
                state.unreadCount = 0;
                state.isClearing = false;
                state.isLoadingMore = false;
                state.hasMore = false;
                state.hasHistory = false;
                state.nextBefore = null;
                state.isShowingHistory = false;
                state.clearingNotificationIds = new Set();
                state.removingNotificationIds = new Set();
                void loadNotificationCenter(centerRoot, { force: true });
            }
        });
    });

    notificationCenterDelegatesStarted = true;
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

function initializeNotificationCenters() {
    document.querySelectorAll(NOTIFICATION_TOGGLE_SELECTOR).forEach((notificationToggle) => {
        wireNotificationCenter(notificationToggle);
    });
}

function initializeHeaderInteractions() {
    initializeNavMenus();
    initializeHeaderSearch();
    initializeNotificationCenters();
    initializeAccountMenus();
    startNavMenuDelegates();
    startNotificationCenterDelegates();
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
