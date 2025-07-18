window.changePrismTheme = (themeUrl) => {
    // Find the existing PrismJS theme link
    let prismThemeLink = document.getElementById("prism-theme");

    if (prismThemeLink) {
        // Update the href to the new theme URL
        prismThemeLink.href = themeUrl;
    } else {
        // If there's no PrismJS theme link, create a new one
        prismThemeLink = document.createElement("link");
        prismThemeLink.id = "prism-theme";
        prismThemeLink.rel = "stylesheet";
        prismThemeLink.href = themeUrl;
        document.head.appendChild(prismThemeLink);
    }
};