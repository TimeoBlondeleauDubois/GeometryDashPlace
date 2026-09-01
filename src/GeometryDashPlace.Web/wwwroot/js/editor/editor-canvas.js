import { drawClassicScene } from "/js/editor/scene-textures.js";

const instances = new WeakMap();

export function initialize(canvas, dotNetReference, options) {
    dispose(canvas);

    const context = canvas.getContext("2d", { alpha: false });
    const definitions = new Map(options.definitions.map(definition => [definition.type, definition]));
    const instance = {
        context,
        definitions,
        objectImages: new Map(),
        backgroundImage: null,
        groundImage: null,
        snapshot: null,
        frame: 0,
        observer: null,
        pointerDownHandler: null,
        dotNetReference
    };

    const requestDraw = () => requestRender(canvas, instance);
    instance.backgroundImage = loadImage(options.backgroundTexturePath, requestDraw);
    instance.groundImage = loadImage(options.groundTexturePath, requestDraw);

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

    context.clearRect(0, 0, state.width, state.height);
    drawClassicScene(context, {
        width: state.width,
        height: state.height,
        groundTop,
        groundTileSize: state.groundTileCells * state.cellSize,
        worldOffsetPixels: state.offsetX * state.cellSize,
        backgroundImage: instance.backgroundImage,
        groundImage: instance.groundImage
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
}

function drawObject(context, instance, state, object, gridToScreenX, gridToScreenY) {
    const definition = instance.definitions.get(object.catalogType);
    const image = instance.objectImages.get(object.catalogType);
    if (!definition || !image?.complete || !image.naturalWidth) {
        return;
    }

    const width = state.cellSize * image.naturalWidth / state.objectTextureUnit;
    const height = state.cellSize * image.naturalHeight / state.objectTextureUnit;
    let centerX = gridToScreenX(object.x + 0.5);
    let centerY = gridToScreenY(object.y + 0.5);
    let offset = definition.yOffset;

    if (object.rotation === 180 || object.rotation === 270) {
        offset *= -1;
    }
    if (object.rotation === 90 || object.rotation === 270) {
        centerX += offset / 30 * state.cellSize;
    } else {
        centerY -= offset / 30 * state.cellSize;
    }

    context.save();
    context.globalAlpha = object.opacity;
    context.translate(centerX, centerY);
    context.rotate(object.rotation * Math.PI / 180);
    context.drawImage(image, -width / 2, -height / 2, width, height);
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
