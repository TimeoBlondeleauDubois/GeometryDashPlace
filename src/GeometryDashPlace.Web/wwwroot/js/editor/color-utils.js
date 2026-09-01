function clampColor(value) {
    return Math.min(255, Math.max(0, Math.round(Number(value) || 0)));
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
