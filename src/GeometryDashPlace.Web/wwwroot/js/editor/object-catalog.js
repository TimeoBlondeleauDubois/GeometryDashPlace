export function readObjectCatalog(objectButtons) {
    return Object.fromEntries(objectButtons.map(button => [
        button.dataset.objectType,
        {
            path: button.dataset.objectPath,
            yOffset: Number(button.dataset.objectYOffset ?? 0),
            canRotate: button.dataset.objectCanRotate !== "false"
        }
    ]));
}
