import { drawClassicScene } from "/js/editor/scene-textures.js";

const instances = new WeakMap();
const defaultBackgroundColor = Object.freeze({ red: 25, green: 117, blue: 220 });
const defaultGroundColor = Object.freeze({ red: 5, green: 126, blue: 255 });

export function initialize(canvas, dotNetReference, options) {
    dispose(canvas);

    const context = canvas.getContext("2d", { alpha: false });
    const definitions = new Map(options.definitions.map(definition => [definition.type, definition]));
    const instance = {
        context,
        definitions,
        objectImages: new Map(),
        avatarImages: new Map(),
        backgroundImage: null,
        groundImage: null,
        freeRotationHandleImage: null,
        snapshot: null,
        frame: 0,
        observer: null,
        pointerDownHandler: null,
        dotNetReference
    };

    const requestDraw = () => requestRender(canvas, instance);
    instance.backgroundImage = loadImage(options.backgroundTexturePath, requestDraw);
    instance.groundImage = loadImage(options.groundTexturePath, requestDraw);
    instance.freeRotationHandleImage = loadImage(options.freeRotationHandlePath, requestDraw);

    for (const definition of options.definitions) {
        instance.objectImages.set(definition.type, loadImage(definition.path, requestDraw));
    }

    instance.observer = new ResizeObserver(() => resizeCanvas(canvas, instance));
    instance.observer.observe(canvas);
    instance.pointerDownHandler = event => {
        if (event.button === 0 || event.button === 1) {
            canvas.setPointerCapture(event.pointerId);
        }
    };
    canvas.addEventListener("pointerdown", instance.pointerDownHandler);
    instances.set(canvas, instance);
    resizeCanvas(canvas, instance);
}

export function render(canvas, snapshot) {
    const instance = instances.get(canvas);
    if (!instance) {
        return;
    }

    instance.snapshot = snapshot;
    requestRender(canvas, instance);
}

export function dispose(canvas) {
    const instance = instances.get(canvas);
    if (!instance) {
        return;
    }

    instance.observer?.disconnect();
    canvas.removeEventListener("pointerdown", instance.pointerDownHandler);
    if (instance.frame) {
        cancelAnimationFrame(instance.frame);
    }
    instances.delete(canvas);
}

function loadImage(path, onLoad) {
    const image = new Image();
    image.addEventListener("load", onLoad);
    image.src = path;
    return image;
}

function resizeCanvas(canvas, instance) {
    const bounds = canvas.getBoundingClientRect();
    const width = Math.max(1, bounds.width);
    const height = Math.max(1, bounds.height);
    const devicePixelRatio = window.devicePixelRatio || 1;
    const pixelWidth = Math.round(width * devicePixelRatio);
    const pixelHeight = Math.round(height * devicePixelRatio);

    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
        canvas.width = pixelWidth;
        canvas.height = pixelHeight;
        instance.context.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0);
    }

    instance.dotNetReference.invokeMethodAsync("OnCanvasResized", width, height, bounds.left, bounds.top);
}

function requestRender(canvas, instance) {
    if (instance.frame || !instance.snapshot) {
        return;
    }

    instance.frame = requestAnimationFrame(() => {
        instance.frame = 0;
        drawEditor(canvas, instance);
    });
}

function drawEditor(canvas, instance) {
    const state = instance.snapshot;
    const context = instance.context;
    if (!state || state.width <= 0 || state.height <= 0) {
        return;
    }

    const gridToScreenX = x => (x - state.offsetX) * state.cellSize;
    const gridToScreenY = y => state.groundBaseline - (y - state.offsetY) * state.cellSize;
    const gridLeft = gridToScreenX(0);
    const gridRight = gridToScreenX(state.columnCount);
    const gridTop = gridToScreenY(state.rowCount);
    const gridBottom = gridToScreenY(0);
    const groundTop = Math.min(Math.max(gridBottom, 0), state.height);
    const backgroundColor = state.backgroundColor ?? defaultBackgroundColor;
    const groundColor = state.groundColor ?? defaultGroundColor;

    context.clearRect(0, 0, state.width, state.height);
    drawClassicScene(context, {
        width: state.width,
        height: state.height,
        groundTop,
        groundTileSize: state.groundTileCells * state.cellSize,
        worldOffsetPixels: state.offsetX * state.cellSize,
        backgroundImage: instance.backgroundImage,
        groundImage: instance.groundImage,
        backgroundColor,
        groundColor
    });

    const visibleLeft = Math.max(0, gridLeft);
    const visibleRight = Math.min(state.width, gridRight);
    const visibleTop = Math.max(0, gridTop);
    const visibleBottom = Math.min(state.height, gridBottom);
    const firstColumn = Math.max(0, Math.floor(state.offsetX));
    const lastColumn = Math.min(state.columnCount, Math.ceil(state.offsetX + state.width / state.cellSize));
    const firstRow = Math.max(0, Math.floor(state.offsetY));
    const lastRow = Math.min(state.rowCount, Math.ceil(state.offsetY + state.groundBaseline / state.cellSize));

    context.beginPath();
    context.strokeStyle = "rgba(5, 20, 38, 0.72)";
    context.lineWidth = 1;

    for (let column = firstColumn; column <= lastColumn; column += 1) {
        const x = Math.round(gridToScreenX(column)) + 0.5;
        context.moveTo(x, visibleTop);
        context.lineTo(x, visibleBottom);
    }

    for (let row = firstRow; row <= lastRow; row += 1) {
        const y = Math.round(gridToScreenY(row)) + 0.5;
        context.moveTo(visibleLeft, y);
        context.lineTo(visibleRight, y);
    }
    context.stroke();

    context.beginPath();
    context.strokeStyle = "rgba(1, 10, 23, 0.82)";
    context.lineWidth = 2;
    const firstMajorColumn = Math.ceil(firstColumn / 10) * 10;
    for (let column = firstMajorColumn; column <= lastColumn; column += 10) {
        const x = Math.round(gridToScreenX(column)) + 0.5;
        context.moveTo(x, visibleTop);
        context.lineTo(x, visibleBottom);
    }
    context.moveTo(visibleLeft, Math.round(gridToScreenY(0)) + 0.5);
    context.lineTo(visibleRight, Math.round(gridToScreenY(0)) + 0.5);
    context.stroke();

    for (const object of state.objects) {
        drawObject(context, instance, state, object, gridToScreenX, gridToScreenY);
    }

    drawCell(context, state, state.hoverCell, gridToScreenX, gridToScreenY,
        "rgba(111, 196, 255, 0.20)", "rgba(174, 228, 255, 0.9)", 2, false);
    drawCell(context, state, state.selectedCell, gridToScreenX, gridToScreenY,
        "rgba(255, 235, 55, 0.2)", "#fff36a", 3, true);

    drawFreeRotationGuide(context, instance, state, gridToScreenX, gridToScreenY);

    for (const presence of state.remotePresences ?? []) {
        drawRemotePresence(canvas, context, instance, state, presence, gridToScreenX, gridToScreenY);
    }
}

function drawRemotePresence(canvas, context, instance, state, presence, gridToScreenX, gridToScreenY) {
    const color = presenceColor(presence.userId);
    if (presence.previewX !== null && presence.previewX !== undefined &&
        presence.previewY !== null && presence.previewY !== undefined) {
        const previewX = gridToScreenX(presence.previewX + 0.5);
        const previewY = gridToScreenY(presence.previewY + 1) - 9;
        drawIdentityBadge(canvas, context, instance, presence, previewX, previewY, color, 0.82);
    }

    if (presence.cursorX === null || presence.cursorX === undefined ||
        presence.cursorY === null || presence.cursorY === undefined) {
        return;
    }

    const cursorX = gridToScreenX(presence.cursorX);
    const cursorY = gridToScreenY(presence.cursorY);
    if (cursorX < -20 || cursorX > state.width + 20 ||
        cursorY < -20 || cursorY > state.height + 20) {
        return;
    }

    context.save();
    context.translate(cursorX, cursorY);
    context.beginPath();
    context.moveTo(0, 0);
    context.lineTo(4, 20);
    context.lineTo(9, 14);
    context.lineTo(15, 24);
    context.lineTo(20, 21);
    context.lineTo(14, 11);
    context.lineTo(22, 9);
    context.closePath();
    context.fillStyle = color;
    context.strokeStyle = "white";
    context.lineWidth = 4;
    context.lineJoin = "round";
    context.stroke();
    context.lineWidth = 2;
    context.strokeStyle = "#07101d";
    context.stroke();
    context.fill();
    context.restore();

    drawIdentityBadge(canvas, context, instance, presence, cursorX + 16, cursorY + 27, color, 1);
}

function drawIdentityBadge(canvas, context, instance, presence, anchorX, anchorY, color, opacity) {
    const username = String(presence.username || "PLAYER").slice(0, 20);
    context.save();
    context.font = "800 11px Arial, sans-serif";
    const avatarSize = 22;
    const height = 28;
    const width = Math.min(170, Math.max(70, context.measureText(username).width + avatarSize + 22));
    const x = Math.min(Math.max(5, anchorX), Math.max(5, canvas.clientWidth - width - 5));
    const y = Math.min(Math.max(5, anchorY - height), Math.max(5, canvas.clientHeight - height - 5));

    context.globalAlpha = opacity;
    roundedRect(context, x, y, width, height, 8);
    context.fillStyle = "rgba(7, 23, 47, 0.94)";
    context.fill();
    context.lineWidth = 2;
    context.strokeStyle = "white";
    context.stroke();
    context.lineWidth = 3;
    context.strokeStyle = color;
    roundedRect(context, x + 2, y + 2, width - 4, height - 4, 6);
    context.stroke();

    const avatarX = x + 5;
    const avatarY = y + 3;
    context.save();
    context.beginPath();
    context.arc(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, 0, Math.PI * 2);
    context.clip();
    const avatar = getAvatarImage(canvas, instance, presence.avatarUrl);
    if (avatar?.complete && avatar.naturalWidth) {
        context.drawImage(avatar, avatarX, avatarY, avatarSize, avatarSize);
    } else {
        context.fillStyle = color;
        context.fillRect(avatarX, avatarY, avatarSize, avatarSize);
        context.fillStyle = "white";
        context.font = "900 12px Arial, sans-serif";
        context.textAlign = "center";
        context.textBaseline = "middle";
        context.fillText(username.charAt(0).toUpperCase() || "?", avatarX + 11, avatarY + 12);
    }
    context.restore();

    context.fillStyle = "white";
    context.font = "800 11px Arial, sans-serif";
    context.textAlign = "left";
    context.textBaseline = "middle";
    context.shadowColor = "black";
    context.shadowOffsetX = 1;
    context.shadowOffsetY = 1;
    context.fillText(username, x + avatarSize + 11, y + height / 2, width - avatarSize - 15);
    context.restore();
}

function getAvatarImage(canvas, instance, path) {
    if (!path) {
        return null;
    }

    let image = instance.avatarImages.get(path);
    if (!image) {
        image = loadImage(path, () => requestRender(canvas, instance));
        instance.avatarImages.set(path, image);
    }
    return image;
}

function presenceColor(userId) {
    let hash = 0;
    for (const character of String(userId)) {
        hash = ((hash << 5) - hash + character.charCodeAt(0)) | 0;
    }
    return `hsl(${Math.abs(hash) % 360} 82% 55%)`;
}

function roundedRect(context, x, y, width, height, radius) {
    const safeRadius = Math.min(radius, width / 2, height / 2);
    context.beginPath();
    context.roundRect(x, y, width, height, safeRadius);
}

function drawObject(context, instance, state, object, gridToScreenX, gridToScreenY) {
    const definition = instance.definitions.get(object.catalogType);
    const image = instance.objectImages.get(object.catalogType);
    if (!definition || !image?.complete || !image.naturalWidth) {
        return;
    }

    const width = state.cellSize * image.naturalWidth / state.objectTextureUnit * object.scaleX;
    const height = state.cellSize * image.naturalHeight / state.objectTextureUnit * object.scaleY;
    let centerX = gridToScreenX(object.x + 0.5);
    let centerY = gridToScreenY(object.y + 0.5);
    const offset = definition.yOffset / 30 * state.cellSize;
    const rotation = object.rotation * Math.PI / 180;
    centerX += Math.sin(rotation) * offset;
    centerY -= Math.cos(rotation) * offset;

    context.save();
    context.globalAlpha = object.opacity;
    context.translate(centerX, centerY);
    context.rotate(rotation);
    context.drawImage(image, -width / 2, -height / 2, width, height);
    context.restore();
}

function drawFreeRotationGuide(context, instance, state, gridToScreenX, gridToScreenY) {
    const guide = state.rotationGuide;
    if (!guide) {
        return;
    }

    const centerX = gridToScreenX(guide.x + 0.5);
    const centerY = gridToScreenY(guide.y + 0.5);
    const radius = guide.radiusCells * state.cellSize;

    context.save();
    context.strokeStyle = "rgba(255, 255, 255, 0.95)";
    context.lineWidth = Math.max(1, state.cellSize * 0.045);
    context.beginPath();
    context.arc(centerX, centerY, radius, 0, Math.PI * 2);
    context.stroke();

    const handleRadians = (guide.rotation - 90) * Math.PI / 180;
    const handleX = centerX + Math.cos(handleRadians) * radius;
    const handleY = centerY + Math.sin(handleRadians) * radius;
    const handleImage = instance.freeRotationHandleImage;
    const handleSize = Math.min(116, Math.max(38, state.cellSize * 2.2));

    if (handleImage?.complete && handleImage.naturalWidth) {
        const handleHeight = handleSize * handleImage.naturalHeight / handleImage.naturalWidth;
        context.drawImage(handleImage,
            handleX - handleSize / 2,
            handleY - handleHeight / 2,
            handleSize,
            handleHeight);
    }

    context.restore();
}

function drawCell(context, state, cell, gridToScreenX, gridToScreenY, fill, stroke, lineWidth, dashed) {
    if (!cell) {
        return;
    }

    const x = gridToScreenX(cell.x);
    const y = gridToScreenY(cell.y + 1);
    context.save();
    context.fillStyle = fill;
    context.fillRect(x + 1, y + 1, state.cellSize - 2, state.cellSize - 2);
    context.strokeStyle = stroke;
    context.lineWidth = lineWidth;
    if (dashed) {
        context.setLineDash([Math.max(3, state.cellSize * 0.18), Math.max(2, state.cellSize * 0.1)]);
    }
    context.strokeRect(x + lineWidth / 2, y + lineWidth / 2,
        state.cellSize - lineWidth, state.cellSize - lineWidth);
    context.restore();
}
