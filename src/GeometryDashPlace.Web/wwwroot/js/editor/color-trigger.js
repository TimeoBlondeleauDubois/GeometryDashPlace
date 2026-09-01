export const COLOR_TRIGGER_TYPE = "color_trigger";

const BACKGROUND_TRIGGER_TYPE = "bg_color_trigger";
const GROUND_TRIGGER_TYPE = "g1_color_trigger";

export function createPendingObject(type, x, y, rotation) {
    const pendingObject = { type, x, y, rotation };

    if (type === COLOR_TRIGGER_TYPE) {
        Object.assign(pendingObject, {
            colorTarget: "background",
            red: 255,
            green: 255,
            blue: 255,
            duration: 0.2,
            rotation: 0
        });
    }

    return pendingObject;
}

export function catalogTypeFor(objectType) {
    return objectType === BACKGROUND_TRIGGER_TYPE || objectType === GROUND_TRIGGER_TYPE
        ? COLOR_TRIGGER_TYPE
        : objectType;
}

export function createEditableObject(confirmedObject) {
    const editableObject = {
        ...confirmedObject,
        type: catalogTypeFor(confirmedObject.type)
    };

    if (editableObject.type === COLOR_TRIGGER_TYPE) {
        editableObject.colorTarget = confirmedObject.type === GROUND_TRIGGER_TYPE
            ? "ground"
            : "background";
    }

    return editableObject;
}

export function createConfirmedObject(pendingObject) {
    const confirmedObject = { ...pendingObject };

    if (confirmedObject.type === COLOR_TRIGGER_TYPE) {
        confirmedObject.type = confirmedObject.colorTarget === "ground"
            ? GROUND_TRIGGER_TYPE
            : BACKGROUND_TRIGGER_TYPE;
        confirmedObject.red = clampColor(confirmedObject.red);
        confirmedObject.green = clampColor(confirmedObject.green);
        confirmedObject.blue = clampColor(confirmedObject.blue);
        confirmedObject.duration = Math.max(0, Number(confirmedObject.duration) || 0);
        delete confirmedObject.colorTarget;
    }

    return confirmedObject;
}

export function hexToRgb(hexColor) {
    const normalized = hexColor.replace("#", "");
    return {
        red: Number.parseInt(normalized.slice(0, 2), 16),
        green: Number.parseInt(normalized.slice(2, 4), 16),
        blue: Number.parseInt(normalized.slice(4, 6), 16)
    };
}

export function rgbToHex(red, green, blue) {
    return `#${[red, green, blue]
        .map(component => clampColor(component).toString(16).padStart(2, "0"))
        .join("")}`.toUpperCase();
}

function clampColor(value) {
    return Math.min(255, Math.max(0, Math.round(Number(value) || 0)));
}
