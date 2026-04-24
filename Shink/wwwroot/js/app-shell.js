(function () {
    "use strict";

    initializePostHog();
    initializeBrowserShell();

    function initializePostHog() {
        if (window.posthog) {
            return;
        }

        var body = document.body;
        if (!body) {
            return;
        }

        var apiKey = body.getAttribute("data-posthog-api-key");
        var hostUrl = body.getAttribute("data-posthog-host-url");
        if (!apiKey || !hostUrl) {
            return;
        }

        var normalizedHostUrl = hostUrl.replace(/\/+$/, "");
        var script = document.createElement("script");
        script.async = true;
        script.src = normalizedHostUrl + "/static/array.js";
        script.onload = function () {
            if (!window.posthog) {
                return;
            }

            window.posthog.init(apiKey, {
                api_host: hostUrl,
                person_profiles: "identified_only",
                capture_pageview: true,
                capture_pageleave: true
            });
        };

        document.head.appendChild(script);
    }

    function initializeBrowserShell() {
        var documentElement = document.documentElement;
        var browserSupport = window.shinkBrowserSupport;

        function getFallback() {
            return document.getElementById("unsupported-browser-fallback");
        }

        function getFallbackDetail() {
            return document.getElementById("unsupported-browser-detail");
        }

        function getAppShellRoot() {
            return document.getElementById("app-shell-root");
        }

        function ensureSupportedShellVisibility() {
            if (documentElement.className.indexOf("unsupported-browser") !== -1) {
                return;
            }

            var appShellRoot = getAppShellRoot();
            if (appShellRoot) {
                appShellRoot.removeAttribute("hidden");
            }

            var fallback = getFallback();
            if (fallback) {
                fallback.hidden = true;
            }
        }

        function finishSupportCheck() {
            documentElement.className = documentElement.className
                .replace(/\bbrowser-support-pending\b/g, "")
                .replace(/\s{2,}/g, " ")
                .trim();

            ensureSupportedShellVisibility();
        }

        document.addEventListener("enhancedload", finishSupportCheck);

        function showUnsupportedFallback(detailMessage) {
            var fallback = getFallback();
            var fallbackDetail = getFallbackDetail();
            var appShellRoot = getAppShellRoot();

            if (documentElement.className.indexOf("unsupported-browser") === -1) {
                documentElement.className += (documentElement.className ? " " : "") + "unsupported-browser";
            }

            if (appShellRoot) {
                appShellRoot.setAttribute("hidden", "hidden");
            }

            if (fallbackDetail && detailMessage) {
                fallbackDetail.textContent = detailMessage;
            }

            if (fallback) {
                fallback.hidden = false;
            }

            finishSupportCheck();
        }

        function loadScript(source) {
            return new Promise(function (resolve, reject) {
                var script = document.createElement("script");
                script.src = source;
                script.type = "text/javascript";
                script.async = false;
                script.onload = function () { resolve(); };
                script.onerror = function () { reject(new Error("Failed to load script: " + source)); };
                document.body.appendChild(script);
            });
        }

        try {
            var supportResult;

            try {
                supportResult = browserSupport && typeof browserSupport.checkSupport === "function"
                    ? browserSupport.checkSupport()
                    : { supported: true };
            } catch (supportError) {
                console.error("Browser support check failed.", supportError);
                showUnsupportedFallback("Ons kon nie jou blaaier se ondersteuning veilig bevestig nie. Gebruik asseblief die nuutste Safari, Chrome, Edge of Firefox.");
                return;
            }

            if (!supportResult.supported) {
                var detailMessage = browserSupport && typeof browserSupport.buildFallbackDetail === "function"
                    ? browserSupport.buildFallbackDetail(supportResult)
                    : "Werk asseblief jou blaaier op en probeer weer.";

                showUnsupportedFallback(detailMessage);
                return;
            }

            var blazorRuntimeSource = supportResult && supportResult.useLegacyBlazorRuntime
                ? "/_framework/bit.blazor.web.es2019.js?v=20260409-2"
                : "/_framework/blazor.web.js?v=20260409-2";

            loadScript("_content/MudBlazor/MudBlazor.min.js")
                .then(function () {
                    return loadScript(blazorRuntimeSource);
                })
                .then(function () {
                    finishSupportCheck();
                })
                .catch(function (scriptError) {
                    console.error("Browser runtime load failed.", scriptError);
                    showUnsupportedFallback("Hierdie bladsy se kernlêers het nie reg gelaai nie. Maak die bladsy heeltemal toe, maak dit weer oop en probeer weer.");
                });
        } catch (bootstrapError) {
            console.error("Browser bootstrap failed.", bootstrapError);
            showUnsupportedFallback("Hierdie bladsy kon nie veilig begin laai nie. Werk asseblief jou blaaier op en probeer weer.");
        }
    }
})();
