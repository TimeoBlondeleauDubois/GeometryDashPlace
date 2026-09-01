const pointerCaptureHandlers = new WeakMap();

export function enablePointerCapture(element) {
    disablePointerCapture(element);

    const handler = event => element.setPointerCapture(event.pointerId);
    pointerCaptureHandlers.set(element, handler);
    element.addEventListener("pointerdown", handler);
}

export function disablePointerCapture(element) {
    const handler = pointerCaptureHandlers.get(element);
    if (handler) {
        element.removeEventListener("pointerdown", handler);
        pointerCaptureHandlers.delete(element);
    }
}

export function horizontalProgress(element, clientX) {
    const bounds = element.getBoundingClientRect();
    return Math.min(Math.max((clientX - bounds.left) / bounds.width, 0), 1);
}
