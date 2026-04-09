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

    function getVersionInfo(match) {
        if (!match) {
            return null;
        }

        var majorVersion = parseInt(match[1], 10);
        var minorVersion = match[2] ? parseInt(match[2], 10) : 0;
        if (!isFinite(majorVersion) || !isFinite(minorVersion)) {
            return null;
        }

        return {
            major: majorVersion,
            minor: minorVersion
        };
    }

    function isVersionBelow(version, major, minor) {
        if (!version) {
            return false;
        }

        if (version.major !== major) {
            return version.major < major;
        }

        return version.minor < minor;
    }

    function getSafariVersionInfo(userAgent) {
        if (!isSafariBrowser(userAgent)) {
            return null;
        }

        return getVersionInfo(/version\/(\d+)(?:\.(\d+))?/i.exec(userAgent));
    }

    function getSafariVersion(userAgent) {
        var version = getSafariVersionInfo(userAgent);
        return version ? version.major : null;
    }

    function getIosVersionInfo(userAgent) {
        if (typeof userAgent !== "string" || userAgent === "") {
            return null;
        }

        var version = getVersionInfo(/(?:CPU(?: iPhone)? OS|iPhone OS) (\d+)(?:[._](\d+))?/i.exec(userAgent));
        if (version) {
            return version;
        }

        if (/macintosh/i.test(userAgent) && /mobile\//i.test(userAgent)) {
            return getSafariVersionInfo(userAgent);
        }

        return null;
    }

    function isOldSafari(userAgent) {
        var safariVersion = getSafariVersionInfo(userAgent);
        return isVersionBelow(safariVersion, 15, 0);
    }

    function isOldIosWebKit(userAgent) {
        var iosVersion = getIosVersionInfo(userAgent);
        return isVersionBelow(iosVersion, 15, 0);
    }

    function shouldUseLegacyBlazorRuntime(userAgent) {
        var iosVersion = getIosVersionInfo(userAgent);
        if (isVersionBelow(iosVersion, 16, 4)) {
            return true;
        }

        var safariVersion = getSafariVersionInfo(userAgent);
        return isVersionBelow(safariVersion, 16, 4);
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
        var iosVersion = getIosVersionInfo(userAgent);
        var unsupportedReason = null;

        if (isOldIosWebKit(userAgent)) {
            unsupportedReason = "old-ios-webkit";
        } else if (isOldSafari(userAgent)) {
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
            iosVersion: iosVersion ? iosVersion.major + "." + iosVersion.minor : null,
            useLegacyBlazorRuntime: shouldUseLegacyBlazorRuntime(userAgent),
            isSafari: isSafariBrowser(userAgent),
            userAgent: userAgent
        };
    }

    function buildFallbackDetail(result) {
        if (!result || result.supported) {
            return "";
        }

        if (result.reasonCode === "old-ios-webkit" && typeof result.iosVersion === "string") {
            return "Jou iPhone of iPad se WebKit-weergawe is te oud vir veilige aanmelding en klankafspeel. Werk asseblief iOS of iPadOS op na weergawe 15 of nuwer.";
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
        shouldUseLegacyBlazorRuntime: function () {
            return shouldUseLegacyBlazorRuntime(getUserAgent());
        },
        getSafariVersion: function () {
            return getSafariVersion(getUserAgent());
        },
        getIosVersion: function () {
            var version = getIosVersionInfo(getUserAgent());
            return version ? version.major + "." + version.minor : null;
        },
        isOldSafari: function () {
            return isOldSafari(getUserAgent());
        },
        lacksModernCrypto: lacksModernCrypto
    };
})(window);
