window.PrismHighlightElement = (element) => {
    Prism.highlightElement(element.querySelector('code'));
};

window.PrismHighlightAll = () => {
    Prism.highlightAll();
};
