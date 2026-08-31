import { hexToRgb, rgbToHex } from "/js/editor/color-trigger.js";

const FULL_CIRCLE = Math.PI * 2;
const SQRT_TWO = Math.sqrt(2);

function clamp(value, minimum = 0, maximum = 1) {
    return Math.min(maximum, Math.max(minimum, value));
}

function rgbToHsv(red, green, blue) {
    const normalizedRed = red / 255;
    const normalizedGreen = green / 255;
    const normalizedBlue = blue / 255;
    const maximum = Math.max(normalizedRed, normalizedGreen, normalizedBlue);
    const minimum = Math.min(normalizedRed, normalizedGreen, normalizedBlue);
    const difference = maximum - minimum;
    let hue = 0;

    if (difference > 0) {
        if (maximum === normalizedRed) {
            hue = 60 * (((normalizedGreen - normalizedBlue) / difference) % 6);
        } else if (maximum === normalizedGreen) {
            hue = 60 * ((normalizedBlue - normalizedRed) / difference + 2);
        } else {
            hue = 60 * ((normalizedRed - normalizedGreen) / difference + 4);
        }
    }

    return {
        hue: (hue + 360) % 360,
        saturation: maximum === 0 ? 0 : difference / maximum,
        value: maximum
    };
}

function hsvToRgb(hue, saturation, value) {
    const chroma = value * saturation;
    const hueSection = hue / 60;
    const secondary = chroma * (1 - Math.abs(hueSection % 2 - 1));
    const offset = value - chroma;
    let red = 0;
    let green = 0;
    let blue = 0;

    if (hueSection < 1) {
        [red, green] = [chroma, secondary];
    } else if (hueSection < 2) {
        [red, green] = [secondary, chroma];
    } else if (hueSection < 3) {
        [green, blue] = [chroma, secondary];
    } else if (hueSection < 4) {
        [green, blue] = [secondary, chroma];
    } else if (hueSection < 5) {
        [red, blue] = [secondary, chroma];
    } else {
        [red, blue] = [chroma, secondary];
    }

    return {
        red: (red + offset) * 255,
        green: (green + offset) * 255,
        blue: (blue + offset) * 255
    };
}

function squareToDisk(horizontal, vertical) {
    return {
        x: horizontal * Math.sqrt(1 - vertical * vertical / 2),
        y: vertical * Math.sqrt(1 - horizontal * horizontal / 2)
    };
}

function diskToSquare(horizontal, vertical) {
    const horizontalSquared = horizontal * horizontal;
    const verticalSquared = vertical * vertical;

    return {
        x: 0.5 * (
            Math.sqrt(Math.max(0, 2 + horizontalSquared - verticalSquared + 2 * SQRT_TWO * horizontal)) -
            Math.sqrt(Math.max(0, 2 + horizontalSquared - verticalSquared - 2 * SQRT_TWO * horizontal))),
        y: 0.5 * (
            Math.sqrt(Math.max(0, 2 - horizontalSquared + verticalSquared + 2 * SQRT_TWO * vertical)) -
            Math.sqrt(Math.max(0, 2 - horizontalSquared + verticalSquared - 2 * SQRT_TWO * vertical)))
    };
}

export function createColorWheel(root, input) {
    const hueSurface = root.querySelector("[data-color-wheel-hue]");
    const colorPlane = root.querySelector("[data-color-wheel-plane]");
    const hueHandle = root.querySelector("[data-color-wheel-hue-handle]");
    const planeHandle = root.querySelector("[data-color-wheel-plane-handle]");
    const selectedSwatch = root.parentElement.querySelector(".selected-color-swatch");
    let hue = 0;
    let saturation = 0;
    let value = 1;
    let huePointerId = null;
    let planePointerId = null;

    function render() {
        const color = hsvToRgb(hue, saturation, value);
        const hexColor = rgbToHex(color.red, color.green, color.blue);
        const hueRadians = hue / 360 * FULL_CIRCLE;
        const hueRadius = 43;
        const hueX = 50 - Math.cos(hueRadians) * hueRadius;
        const hueY = 50 + Math.sin(hueRadians) * hueRadius;
        const planePosition = squareToDisk(1 - saturation * 2, 1 - value * 2);

        root.style.setProperty("--picker-color", `hsl(${hue}deg 100% 50%)`);
        root.style.setProperty("--selected-color", hexColor);
        selectedSwatch.style.backgroundColor = hexColor;
        hueHandle.style.left = `${hueX}%`;
        hueHandle.style.top = `${hueY}%`;
        planeHandle.style.left = `${(planePosition.x + 1) * 50}%`;
        planeHandle.style.top = `${(planePosition.y + 1) * 50}%`;
        hueSurface.setAttribute("aria-valuenow", String(Math.round(hue)));
        input.value = hexColor;
    }

    function emitColor() {
        render();
        input.dispatchEvent(new Event("input", { bubbles: true }));
    }

    function setHueFromPointer(event) {
        const bounds = hueSurface.getBoundingClientRect();
        const horizontal = event.clientX - bounds.left - bounds.width / 2;
        const vertical = event.clientY - bounds.top - bounds.height / 2;
        const angle = Math.atan2(vertical, horizontal);
        hue = ((Math.PI - angle) / FULL_CIRCLE * 360 + 360) % 360;
        emitColor();
    }

    function setPlaneFromPointer(event) {
        const bounds = colorPlane.getBoundingClientRect();
        let horizontal = (event.clientX - bounds.left) / bounds.width * 2 - 1;
        let vertical = (event.clientY - bounds.top) / bounds.height * 2 - 1;
        const distance = Math.hypot(horizontal, vertical);

        if (distance > 0.98) {
            horizontal = horizontal / distance * 0.98;
            vertical = vertical / distance * 0.98;
        }

        const squarePosition = diskToSquare(horizontal, vertical);
        saturation = clamp((1 - squarePosition.x) / 2);
        value = clamp((1 - squarePosition.y) / 2);
        emitColor();
    }

    function onHuePointerDown(event) {
        if (event.button !== 0 || event.target.closest("[data-color-wheel-plane]")) {
            return;
        }

        huePointerId = event.pointerId;
        hueSurface.setPointerCapture(event.pointerId);
        setHueFromPointer(event);
        event.preventDefault();
    }

    function onHuePointerMove(event) {
        if (event.pointerId === huePointerId) {
            setHueFromPointer(event);
            event.preventDefault();
        }
    }

    function finishHuePointer(event) {
        if (event.pointerId === huePointerId) {
            huePointerId = null;
        }
    }

    function onPlanePointerDown(event) {
        if (event.button !== 0) {
            return;
        }

        planePointerId = event.pointerId;
        colorPlane.setPointerCapture(event.pointerId);
        setPlaneFromPointer(event);
        event.stopPropagation();
        event.preventDefault();
    }

    function onPlanePointerMove(event) {
        if (event.pointerId === planePointerId) {
            setPlaneFromPointer(event);
            event.stopPropagation();
            event.preventDefault();
        }
    }

    function finishPlanePointer(event) {
        if (event.pointerId === planePointerId) {
            planePointerId = null;
            event.stopPropagation();
        }
    }

    function onHueKeyDown(event) {
        const step = event.shiftKey ? 10 : 1;

        if (event.key === "ArrowLeft" || event.key === "ArrowDown") {
            hue = (hue - step + 360) % 360;
        } else if (event.key === "ArrowRight" || event.key === "ArrowUp") {
            hue = (hue + step) % 360;
        } else {
            return;
        }

        emitColor();
        event.preventDefault();
    }

    function onPlaneKeyDown(event) {
        const step = event.shiftKey ? 0.1 : 0.02;

        switch (event.key) {
            case "ArrowLeft":
                saturation = clamp(saturation + step);
                break;
            case "ArrowRight":
                saturation = clamp(saturation - step);
                break;
            case "ArrowUp":
                value = clamp(value + step);
                break;
            case "ArrowDown":
                value = clamp(value - step);
                break;
            default:
                return;
        }

        emitColor();
        event.preventDefault();
    }

    hueSurface.addEventListener("pointerdown", onHuePointerDown);
    hueSurface.addEventListener("pointermove", onHuePointerMove);
    hueSurface.addEventListener("pointerup", finishHuePointer);
    hueSurface.addEventListener("pointercancel", finishHuePointer);
    hueSurface.addEventListener("keydown", onHueKeyDown);
    colorPlane.addEventListener("pointerdown", onPlanePointerDown);
    colorPlane.addEventListener("pointermove", onPlanePointerMove);
    colorPlane.addEventListener("pointerup", finishPlanePointer);
    colorPlane.addEventListener("pointercancel", finishPlanePointer);
    colorPlane.addEventListener("keydown", onPlaneKeyDown);

    return {
        setColor(hexColor) {
            const color = hexToRgb(hexColor);
            const hsv = rgbToHsv(color.red, color.green, color.blue);
            if (hsv.saturation > 0 && hsv.value > 0) {
                hue = hsv.hue;
            }
            saturation = hsv.saturation;
            value = hsv.value;
            render();
        },
        dispose() {
            hueSurface.removeEventListener("pointerdown", onHuePointerDown);
            hueSurface.removeEventListener("pointermove", onHuePointerMove);
            hueSurface.removeEventListener("pointerup", finishHuePointer);
            hueSurface.removeEventListener("pointercancel", finishHuePointer);
            hueSurface.removeEventListener("keydown", onHueKeyDown);
            colorPlane.removeEventListener("pointerdown", onPlanePointerDown);
            colorPlane.removeEventListener("pointermove", onPlanePointerMove);
            colorPlane.removeEventListener("pointerup", finishPlanePointer);
            colorPlane.removeEventListener("pointercancel", finishPlanePointer);
            colorPlane.removeEventListener("keydown", onPlaneKeyDown);
        }
    };
}
