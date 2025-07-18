window.PrismHighlightElement = (element) => {
    Prism.highlightElement(element.querySelector('code'));
};

window.PrismHighlightAll = () => {
    Prism.highlightAll();
};

function setCookie(name, value, days) {
    var expires = "";
    if (days) {
        var date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = "; expires=" + date.toUTCString();
    }
    document.cookie = name + "=" + (value || "") + expires + "; path=/";
}

function getCookie(name) {
    var nameEQ = name + "=";
    var ca = document.cookie.split(';');
    for (var i = 0; i < ca.length; i++) {
        var c = ca[i];
        while (c.charAt(0) == ' ') c = c.substring(1, c.length);
        if (c.indexOf(nameEQ) == 0) return c.substring(nameEQ.length, c.length);
    }
    return null;
}


window.fileSystemHelpers = {
    directoryHandles: {},
    directoryHandleIdCounter: 0,

    async getDirectoryHandle() {
        if (!('showDirectoryPicker' in window)) {
            console.error('Directory picker not supported in this browser.');
            return {
                success: false,
                error: 'Directory picker not supported in this browser.'
            };
        }

        try {
            // 1. Get the root directory from the user
            const rootDirectoryHandle = await window.showDirectoryPicker();
            const directoryId = ++this.directoryHandleIdCounter;
            this.directoryHandles[directoryId] = rootDirectoryHandle;

            // 2. Traverse into single-child subfolders automatically
            let currentHandle = rootDirectoryHandle;
            let pathSegments = [rootDirectoryHandle.name]; // Start path with root folder's name

            while (true) {
                const subdirectoriesInCurrent = [];
                for await (const entry of currentHandle.values()) {
                    if (entry.kind === 'directory') {
                        subdirectoriesInCurrent.push(entry);
                    }
                }

                if (subdirectoriesInCurrent.length === 1) {
                    // If there's only one subfolder, go deeper
                    currentHandle = subdirectoriesInCurrent[0];
                    pathSegments.push(currentHandle.name);
                } else {
                    // If there are 0 or more than 1, stop here. `currentHandle` is our target.
                    break;
                }
            }

            // 3. List all subdirectories from the final target folder
            const finalSubdirectories = [];
            const basePath = pathSegments.join('/');

            for await (const entry of currentHandle.values()) {
                if (entry.kind === 'directory') {
                    // Construct the full path for each subdirectory found
                    finalSubdirectories.push(`${basePath}/${entry.name}`);
                }
            }

            // 4. Return all the required information in one object
            return {
                success: true,
                directoryId: directoryId,
                name: rootDirectoryHandle.name,
                subdirectories: finalSubdirectories
            };

        } catch (err) {
            // Catches user cancellation or other errors.
            return { success: false, error: err.message };
        }
    },

    async readFileContent(directoryId, fileName) {
        try {
            const directoryHandle = this.directoryHandles[directoryId];
            if (!directoryHandle)
                throw new Error('Invalid directory handle.');

            // normalize the path separators, remove any leading slashes, split the path into segments
            const normalizedPath = fileName.replace(/\\/g, '/');
            const trimmedPath = normalizedPath.replace(/^\/+/, '');
            const pathSegments = trimmedPath.split('/');

            // traverse the directories
            let currentHandle = directoryHandle;
            for (let i = 0; i < pathSegments.length; i++) {
                const isLastSegment = i === pathSegments.length - 1;
                const segmentName = pathSegments[i];

                if (isLastSegment) {   
                    const fileHandle = await currentHandle.getFileHandle(segmentName, { create: false });

                    const filePermission = await fileHandle.queryPermission({ mode: 'read' });
                    if (filePermission === 'denied') {
                        const permissionResult = await fileHandle.requestPermission({ mode: 'read' });
                        if (permissionResult !== 'granted') {
                            throw new Error("Permission to read the file was denied.");
                        }
                    }

                    const fileData = await fileHandle.getFile();
                    const fileContent = await fileData.text();

                    return fileContent;
                } else {
                    currentHandle = await currentHandle.getDirectoryHandle(segmentName, { create: false });
                }
            }

            throw new Error("File path traversal did not find a file.");
        } catch (error) {
            console.error(error);
            throw error;
        }
    },

    async getSarifFilesFromDirectory(directoryId) {
        const directoryHandle = this.directoryHandles[directoryId];
        if (!directoryHandle) {
            throw new Error('Invalid directory handle.');
        }

        const sarifFiles = [];
        const maxFiles = 10;
        const maxSize = 25 * 1024 * 1024; // 25MB

        async function getSarifFilesInFolder(folderHandle) {
            for await (const [name, handle] of folderHandle.entries()) {
                if (sarifFiles.length >= maxFiles)
                    break;

                if (handle.kind === 'file' && name.endsWith('.sarif')) {
                    const file = await handle.getFile();

                    if (file.size <= maxSize) {
                        sarifFiles.push({
                            name: file.name,
                            byteBuffer: new Uint8Array(await file.arrayBuffer())
                        });
                    }
                } else if (handle.kind === 'directory' && name === '.sarif') {
                    await getSarifFilesInFolder(handle);
                }

                if (sarifFiles.length >= maxFiles)
                    break;
            }
        }

        // Root level search for .sarif files
        for await (const [name, handle] of directoryHandle.entries()) {
            if (sarifFiles.length >= maxFiles)
                break;

            if (handle.kind === 'file' && name.endsWith('.sarif')) {
                const file = await handle.getFile();

                if (file.size <= maxSize) {
                    sarifFiles.push({
                        name: file.name,
                        byteBuffer: new Uint8Array(await file.arrayBuffer())
                    });
                }
            } else if (handle.kind === 'directory' && name === '.sarif') {
                await getSarifFilesInFolder(handle);
            }

            if (sarifFiles.length >= maxFiles)
                break;
        }

        return sarifFiles;
    },

    
    async canAccessSubfolder(directoryId, subfolderName) {
        try {
            const directoryHandle = this.directoryHandles[directoryId];
            const subHandle = await directoryHandle.getDirectoryHandle(subfolderName, { create: false });
            return true;
        } catch (e) {
            return false;
        }
    }
};
