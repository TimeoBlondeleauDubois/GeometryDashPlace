import { createColorWheel } from "/js/editor/color-wheel.js";

const instances = new WeakMap();

export function initialize(root, input) {
    dispose(root);
    instances.set(root, createColorWheel(root, input));
}

export function setColor(root, color) {
    instances.get(root)?.setColor(color);
}

export function dispose(root) {
    const instance = instances.get(root);
    instance?.dispose();
    instances.delete(root);
}
