window.downloadFile = (fileName, contentType, content) => {
    // CSV gets a UTF-8 byte-order mark so Excel opens accented text correctly; the parsers in this project strip it.
    const parts = /^text\/csv\b/i.test(contentType) ? ['\uFEFF', content] : [content];
    const blob = new Blob(parts, { type: contentType });

    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.href = url;
    link.download = fileName;

    // Append to the document body (required for Firefox)
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Revoke later, not immediately: some browsers start the download asynchronously and an
    // immediately revoked URL aborts it.
    setTimeout(() => URL.revokeObjectURL(url), 10000);
};
