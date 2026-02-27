window.mcpUiBridge = {
    dotNetRef: null,
    channel: "sarifintown.mcp.v1",
    targetWindow: null,
    targetOrigin: "*",
    hostOrigin: null,
    boundHandler: null,

    start(dotNetRef, options) {
        this.stop();

        this.dotNetRef = dotNetRef;
        this.channel = options?.channel || "sarifintown.mcp.v1";
        this.targetOrigin = options?.targetOrigin || "*";
        this.hostOrigin = null;
        this.targetWindow = window.parent && window.parent !== window ? window.parent : window;

        this.boundHandler = (event) => this.handleMessage(event);
        window.addEventListener("message", this.boundHandler);
    },

    stop() {
        if (this.boundHandler) {
            window.removeEventListener("message", this.boundHandler);
        }

        this.boundHandler = null;
        this.hostOrigin = null;
        this.dotNetRef = null;
    },

    send(type, payload, requestId) {
        if (!this.targetWindow) {
            return;
        }

        const envelope = {
            channel: this.channel,
            type,
            requestId: requestId || null,
            payload: payload || {}
        };

        const postTargetOrigin = this.hostOrigin || this.targetOrigin;
        this.targetWindow.postMessage(envelope, postTargetOrigin);
    },

    handleMessage(event) {
        const data = event.data;

        if (!data || typeof data !== "object") {
            return;
        }

        if (this.targetOrigin !== "*" && event.origin !== this.targetOrigin) {
            return;
        }

        if (this.targetOrigin === "*") {
            if (!this.hostOrigin) {
                this.hostOrigin = event.origin;
            }

            if (event.origin !== this.hostOrigin) {
                return;
            }
        }

        if (data.channel !== this.channel) {
            return;
        }

        if (!this.dotNetRef) {
            return;
        }

        this.dotNetRef.invokeMethodAsync("ReceiveHostMessage", JSON.stringify(data));
    }
};
