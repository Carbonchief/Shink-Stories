(function (global) {
    "use strict";

    function getUserAgent() {
        if (!global.navigator || typeof global.navigator.userAgent !== "string") {
            return "";
        }

        return global.navigator.userAgent;
    }

    function isSafariBrowser(userAgent) {
        if (typeof userAgent !== "string" || userAgent === "") {
            return false;
        }

        var normalized = userAgent.toLowerCase();
        return normalized.indexOf("safari") !== -1 &&
            normalized.indexOf("chrome") === -1 &&
            normalized.indexOf("chromium") === -1 &&
            normalized.indexOf("crios") === -1 &&
            normalized.indexOf("edg") === -1 &&
            normalized.indexOf("opr") === -1 &&
            normalized.indexOf("android") === -1;
    }

    function getSafariVersion(userAgent) {
        if (!isSafariBrowser(userAgent)) {
            return null;
        }

        var match = /version\/(\d+)(?:\.\d+)?/i.exec(userAgent);
        if (!match) {
            return null;
        }

        var majorVersion = parseInt(match[1], 10);
        return isFinite(majorVersion) ? majorVersion : null;
    }

    function isOldSafari(userAgent) {
        var safariVersion = getSafariVersion(userAgent);
        return typeof safariVersion === "number" && safariVersion < 15;
    }

    function lacksModernCrypto() {
        var cryptoObject = global.crypto;
        return !cryptoObject ||
            typeof cryptoObject.getRandomValues !== "function" ||
            !cryptoObject.subtle;
    }

    function lacksRequiredBrowserFeatures() {
        var documentObject = global.document;
        if (!documentObject) {
            return true;
        }

        var probeScript = documentObject.createElement ? documentObject.createElement("script") : null;
        return typeof documentObject.querySelector !== "function" ||
            typeof documentObject.addEventListener !== "function" ||
            typeof global.addEventListener !== "function" ||
            typeof global.Promise === "undefined" ||
            typeof global.fetch !== "function" ||
            typeof global.URL !== "function" ||
            typeof global.WeakMap === "undefined" ||
            typeof global.MutationObserver === "undefined" ||
            typeof global.requestAnimationFrame !== "function" ||
            typeof global.AbortController === "undefined" ||
            !probeScript ||
            !("noModule" in probeScript);
    }

    function checkSupport() {
        var userAgent = getUserAgent();
        var safariVersion = getSafariVersion(userAgent);
        var unsupportedReason = null;

        if (isOldSafari(userAgent)) {
            unsupportedReason = "old-safari";
        } else if (lacksModernCrypto()) {
            unsupportedReason = "modern-crypto-missing";
        } else if (lacksRequiredBrowserFeatures()) {
            unsupportedReason = "browser-features-missing";
        }

        return {
            supported: unsupportedReason === null,
            reasonCode: unsupportedReason,
            safariVersion: safariVersion,
            isSafari: isSafariBrowser(userAgent),
            userAgent: userAgent
        };
    }

    function buildFallbackDetail(result) {
        if (!result || result.supported) {
            return "";
        }

        if (result.reasonCode === "old-safari" && typeof result.safariVersion === "number") {
            return "Jou Safari-weergawe is te oud vir veilige aanmelding en klankafspeel. Werk asseblief Safari op na weergawe 15 of nuwer.";
        }

        if (result.reasonCode === "modern-crypto-missing") {
            return "Jou blaaier ondersteun nie die moderne sekuriteitfunksies wat Schink Stories vir veilige aanmelding benodig nie.";
        }

        if (result.reasonCode === "browser-features-missing") {
            return "Jou blaaier ondersteun nie die moderne webfunksies wat hierdie toepassing nodig het om betroubaar te werk nie.";
        }

        return "Hierdie blaaier word nie meer volledig ondersteun nie.";
    }

    global.shinkBrowserSupport = {
        checkSupport: checkSupport,
        buildFallbackDetail: buildFallbackDetail,
        getSafariVersion: function () {
            return getSafariVersion(getUserAgent());
        },
        isOldSafari: function () {
            return isOldSafari(getUserAgent());
        },
        lacksModernCrypto: lacksModernCrypto
    };
})(window);
