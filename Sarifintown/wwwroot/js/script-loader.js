window.scriptLoader = {
    loaded: {},

    ensure(src) {
        if (!src) {
            throw new Error("Script source is required.");
        }

        if (this.loaded[src]) {
            return this.loaded[src];
        }

        this.loaded[src] = new Promise((resolve, reject) => {
            const existing = document.querySelector(`script[src='${src}']`);
            if (existing) {
                resolve();
                return;
            }

            const script = document.createElement("script");
            script.src = src;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error(`Failed to load script: ${src}`));
            document.body.appendChild(script);
        });

        return this.loaded[src];
    }
};
