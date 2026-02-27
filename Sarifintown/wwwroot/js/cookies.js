window.setCookie = (name, value, days) => {
    let expires = "";
    if (days) {
        const date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = `; expires=${date.toUTCString()}`;
    }

    document.cookie = `${name}=${value || ""}${expires}; path=/`;
};

window.getCookie = (name) => {
    const nameEq = `${name}=`;
    const parts = document.cookie.split(';');

    for (let i = 0; i < parts.length; i++) {
        let part = parts[i];
        while (part.charAt(0) === ' ') {
            part = part.substring(1, part.length);
        }

        if (part.indexOf(nameEq) === 0) {
            return part.substring(nameEq.length, part.length);
        }
    }

    return null;
};
