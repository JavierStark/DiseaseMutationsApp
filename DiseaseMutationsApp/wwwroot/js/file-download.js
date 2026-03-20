window.downloadFile = (fileName, contentType, content) => {
    // Create the blob with the given content type
    const blob = new Blob([content], { type: contentType });
    
    // Create a link element
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    
    // Append to the document body (required for Firefox)
    document.body.appendChild(link);
    
    // Simulate click
    link.click();
    
    // Clean up
    document.body.removeChild(link);
    URL.revokeObjectURL(link.href);
};

