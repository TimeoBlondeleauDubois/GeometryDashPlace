import { EDITOR_CONFIG } from "/js/editor/editor-config.js";
import { readObjectCatalog } from "/js/editor/object-catalog.js";
import { drawClassicScene, loadSceneTexture } from "/js/editor/scene-textures.js";
import { createColorWheel } from "/js/editor/color-wheel.js";
import {
    COLOR_TRIGGER_TYPE,
    catalogTypeFor,
    createConfirmedObject,
    createPendingObject,
    hexToRgb,
    rgbToHex
} from "/js/editor/color-trigger.js";

const {
    columnCount: COLUMN_COUNT,
    rowCount: ROW_COUNT,
    minZoom: MIN_ZOOM,
    maxZoom: MAX_ZOOM,
    groundTileCells: GROUND_TILE_CELLS,
    backgroundTexturePath: BACKGROUND_TEXTURE_PATH,
    groundTexturePath: GROUND_TEXTURE_PATH,
    objectTextureUnit: OBJECT_TEXTURE_UNIT,
    palettePageSize: PALETTE_PAGE_SIZE
} = EDITOR_CONFIG;
const editorInstances = new WeakMap();

export function initializeEditorGrid(root) {
    if (editorInstances.has(root)) {
        return;
    }

    const canvas = root.querySelector("canvas");
    const context = canvas.getContext("2d", { alpha: false });
    const coordinateValue = root.querySelector("[data-coordinate-value]");
    const zoomValue = root.querySelector("[data-zoom-value]");
    const objectCountValue = root.querySelector("[data-object-count]");
    const zoomInButton = root.querySelector('[data-editor-action="zoom-in"]');
    const zoomOutButton = root.querySelector('[data-editor-action="zoom-out"]');
    const timeline = root.querySelector("[data-editor-timeline]");
    const timelineHandle = root.querySelector("[data-timeline-handle]");
    const objectButtons = [...root.querySelectorAll("[data-object-type]")];
    const selectedObjectName = root.querySelector("[data-selected-object-name]");
    const selectedRotation = root.querySelector("[data-selected-rotation]");
    const palettePages = root.querySelector("[data-palette-pages]");
    const previousPaletteButton = root.querySelector('[data-palette-action="previous"]');
    const nextPaletteButton = root.querySelector('[data-palette-action="next"]');
    const rotateObjectButtons = [...root.querySelectorAll('[data-editor-action="rotate-object"]')];
    const editorTabButtons = [...root.querySelectorAll("[data-editor-tab]")];
    const editorPanels = [...root.querySelectorAll("[data-editor-panel]")];
    const placementControls = root.querySelector("[data-placement-controls]");
    const pendingPosition = root.querySelector("[data-pending-position]");
    const movementButtons = [...root.querySelectorAll("[data-move-x][data-move-y]")];
    const confirmPlacementButton = root.querySelector('[data-editor-action="confirm-placement"]');
    const colorTriggerSettings = root.querySelector("[data-color-trigger-settings]");
    const colorWheelRoot = root.querySelector("[data-color-wheel]");
    const colorTriggerTargetButtons = [...root.querySelectorAll("[data-color-trigger-target]")];
    const colorTriggerInput = root.querySelector("[data-color-trigger-color]");
    const colorTriggerValue = root.querySelector("[data-color-trigger-color-value]");
    const colorTriggerDurationInput = root.querySelector("[data-color-trigger-duration]");
    const colorWheel = createColorWheel(colorWheelRoot, colorTriggerInput);
    const objectCatalog = readObjectCatalog(objectButtons);

    const state = {
        width: 0,
        height: 0,
        baseCellSize: 30,
        zoom: 1,
        offsetX: 0,
        offsetY: 0,
        pointerId: null,
        pointerX: 0,
        pointerY: 0,
        dragDistance: 0,
        hoverCell: null,
        selectedCell: null,
        objects: new Map(),
        pendingObject: null,
        selectedObjectType: "block",
        selectedRotation: 0,
        palettePage: 0,
        editorTab: "build",
        timelinePointerId: null,
        timelinePointerX: 0,
        timelineStartProgress: 0,
        frame: 0
    };

    const objectImages = new Map();
    const backgroundImage = loadSceneTexture(BACKGROUND_TEXTURE_PATH, requestDraw);
    const groundImage = loadSceneTexture(GROUND_TEXTURE_PATH, requestDraw);

    for (const [type, definition] of Object.entries(objectCatalog)) {
        const image = new Image();
        image.addEventListener("load", requestDraw);
        image.src = definition.path;
        objectImages.set(type, image);
    }

    function cellSize() {
        return state.baseCellSize * state.zoom;
    }

    function groundBaseline() {
        return state.height - GROUND_TILE_CELLS * state.baseCellSize;
    }

    function axisOffset(value, totalCells, visibleCells) {
        if (visibleCells >= totalCells) {
            return (totalCells - visibleCells) / 2;
        }

        return Math.min(Math.max(value, 0), totalCells - visibleCells);
    }

    function clampCamera() {
        const size = cellSize();
        state.offsetX = axisOffset(state.offsetX, COLUMN_COUNT, state.width / size);
        const visibleRows = groundBaseline() / size;
        state.offsetY = visibleRows >= ROW_COUNT
            ? 0
            : Math.min(Math.max(state.offsetY, 0), ROW_COUNT - visibleRows);
    }

    function horizontalTravelCells() {
        return Math.max(0, COLUMN_COUNT - state.width / cellSize());
    }

    function cameraProgress() {
        const travel = horizontalTravelCells();
        return travel > 0 ? state.offsetX / travel : 0;
    }

    function updateTimeline() {
        const progress = Math.min(Math.max(cameraProgress(), 0), 1);
        const progressPercent = progress * 100;
        const trackProgressPercent = progressPercent * 0.956;
        timeline.style.setProperty("--timeline-position", `${2.2 + trackProgressPercent}%`);
        timeline.setAttribute("aria-valuenow", String(Math.round(progressPercent)));
        timeline.setAttribute("aria-valuetext", `Position ${Math.round(progressPercent)} %`);
    }

    function setTimelineProgress(progress) {
        const normalizedProgress = Math.min(Math.max(progress, 0), 1);
        state.offsetX = normalizedProgress * horizontalTravelCells();
        clampCamera();
        updateTimeline();
        requestDraw();
    }

    function gridToScreenX(x) {
        return (x - state.offsetX) * cellSize();
    }

    function gridToScreenY(y) {
        return groundBaseline() - (y - state.offsetY) * cellSize();
    }

    function screenToCell(x, y) {
        const size = cellSize();
        const column = Math.floor(state.offsetX + x / size);
        const row = Math.floor(state.offsetY + (groundBaseline() - y) / size);

        if (column < 0 || column >= COLUMN_COUNT || row < 0 || row >= ROW_COUNT) {
            return null;
        }

        return { x: column, y: row };
    }

    function drawCell(cell, fill, stroke, lineWidth) {
        if (!cell) {
            return;
        }

        const size = cellSize();
        const x = gridToScreenX(cell.x);
        const y = gridToScreenY(cell.y + 1);

        context.fillStyle = fill;
        context.fillRect(x + 1, y + 1, size - 2, size - 2);
        context.strokeStyle = stroke;
        context.lineWidth = lineWidth;
        context.strokeRect(x + lineWidth / 2, y + lineWidth / 2, size - lineWidth, size - lineWidth);
    }

    function cellKey(cell) {
        return `${cell.x}:${cell.y}`;
    }

    function drawPlacedObject(object, opacity = 1) {
        const catalogType = catalogTypeFor(object.type);
        const definition = objectCatalog[catalogType];
        const image = objectImages.get(catalogType);

        if (!definition || !image?.complete || !image.naturalWidth) {
            return;
        }

        const size = cellSize();
        const width = size * image.naturalWidth / OBJECT_TEXTURE_UNIT;
        const height = size * image.naturalHeight / OBJECT_TEXTURE_UNIT;
        let centerX = gridToScreenX(object.x + 0.5);
        let centerY = gridToScreenY(object.y + 0.5);
        let offset = definition.yOffset;

        if (object.rotation === 180 || object.rotation === 270) {
            offset *= -1;
        }

        if (object.rotation === 90 || object.rotation === 270) {
            centerX += offset / 30 * size;
        } else {
            centerY -= offset / 30 * size;
        }

        context.save();
        context.globalAlpha = opacity;
        context.translate(centerX, centerY);
        context.rotate(object.rotation * Math.PI / 180);
        context.drawImage(image, -width / 2, -height / 2, width, height);
        context.restore();
    }

    function drawPlacedObjects(firstColumn, lastColumn, firstRow, lastRow) {
        for (const object of state.objects.values()) {
            if (object.x < firstColumn - 2 || object.x > lastColumn + 2 ||
                object.y < firstRow - 3 || object.y > lastRow + 3) {
                continue;
            }

            drawPlacedObject(object);
        }
    }

    function drawPendingCell(cell) {
        if (!cell) {
            return;
        }

        const size = cellSize();
        const x = gridToScreenX(cell.x);
        const y = gridToScreenY(cell.y + 1);

        context.save();
        context.fillStyle = "rgba(255, 235, 55, 0.2)";
        context.fillRect(x + 1, y + 1, size - 2, size - 2);
        context.strokeStyle = "#fff36a";
        context.lineWidth = 3;
        context.setLineDash([Math.max(3, size * 0.18), Math.max(2, size * 0.1)]);
        context.strokeRect(x + 1.5, y + 1.5, size - 3, size - 3);
        context.restore();
    }

    function draw() {
        state.frame = 0;
        const size = cellSize();

        const gridLeft = gridToScreenX(0);
        const gridRight = gridToScreenX(COLUMN_COUNT);
        const gridTop = gridToScreenY(ROW_COUNT);
        const gridBottom = gridToScreenY(0);
        const sceneGroundTop = Math.min(Math.max(gridBottom, 0), state.height);

        context.clearRect(0, 0, state.width, state.height);
        drawClassicScene(context, {
            width: state.width,
            height: state.height,
            groundTop: sceneGroundTop,
            groundTileSize: GROUND_TILE_CELLS * size,
            worldOffsetPixels: state.offsetX * size,
            backgroundImage,
            groundImage
        });

        const visibleLeft = Math.max(0, gridLeft);
        const visibleRight = Math.min(state.width, gridRight);
        const visibleTop = Math.max(0, gridTop);
        const visibleBottom = Math.min(state.height, gridBottom);

        const firstColumn = Math.max(0, Math.floor(state.offsetX));
        const lastColumn = Math.min(COLUMN_COUNT, Math.ceil(state.offsetX + state.width / size));
        const firstRow = Math.max(0, Math.floor(state.offsetY));
        const lastRow = Math.min(ROW_COUNT, Math.ceil(state.offsetY + groundBaseline() / size));

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

        drawPlacedObjects(firstColumn, lastColumn, firstRow, lastRow);

        if (state.pendingObject) {
            drawPlacedObject(state.pendingObject, 0.62);
        }

        drawCell(state.hoverCell, "rgba(111, 196, 255, 0.20)", "rgba(174, 228, 255, 0.9)", 2);
        drawPendingCell(state.selectedCell);
    }

    function requestDraw() {
        if (!state.frame) {
            state.frame = requestAnimationFrame(draw);
        }
    }

    function updateStatus() {
        coordinateValue.textContent = state.hoverCell
            ? `Case ${state.hoverCell.x}, ${state.hoverCell.y}`
            : state.selectedCell
                ? `Case ${state.selectedCell.x}, ${state.selectedCell.y}`
                : "Case —";
        zoomValue.textContent = `Zoom ${Math.round(state.zoom * 100)} %`;
        objectCountValue.textContent = `${state.objects.size} objet${state.objects.size === 1 ? "" : "s"}`;
    }

    function updateToolStatus() {
        const selectedButton = objectButtons.find(
            button => button.dataset.objectType === state.selectedObjectType);
        const canRotate = selectedObjectCanRotate();

        selectedObjectName.textContent = selectedButton?.dataset.objectName ?? "Objet";
        selectedRotation.textContent = canRotate
            ? `Rotation ${state.selectedRotation}°`
            : "Rotation fixe";
        for (const button of rotateObjectButtons) {
            button.disabled = !canRotate || !state.pendingObject;
        }
    }

    function selectedObjectCanRotate() {
        return objectCatalog[state.selectedObjectType]?.canRotate !== false;
    }

    function updatePlacementControls() {
        const pending = state.pendingObject;
        placementControls.classList.toggle("is-active", Boolean(pending));
        placementControls.setAttribute("aria-disabled", String(!pending));
        pendingPosition.textContent = pending
            ? `Aperçu : case ${pending.x}, ${pending.y}`
            : "Sélectionne une case";
        confirmPlacementButton.disabled = !pending;

        for (const button of rotateObjectButtons) {
            button.disabled = !pending || !selectedObjectCanRotate();
        }

        for (const button of movementButtons) {
            const deltaX = Number(button.dataset.moveX);
            const deltaY = Number(button.dataset.moveY);
            const nextX = pending ? pending.x + deltaX : -1;
            const nextY = pending ? pending.y + deltaY : -1;
            button.disabled = !pending || nextX < 0 || nextX >= COLUMN_COUNT ||
                nextY < 0 || nextY >= ROW_COUNT;
        }

        updateColorTriggerSettings();
    }

    function updateColorTriggerSettings() {
        const pending = state.pendingObject;
        const isColorTrigger = pending?.type === COLOR_TRIGGER_TYPE;
        colorTriggerSettings.hidden = !isColorTrigger;

        if (!isColorTrigger) {
            return;
        }

        for (const button of colorTriggerTargetButtons) {
            const isActive = button.dataset.colorTriggerTarget === pending.colorTarget;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-pressed", String(isActive));
        }

        const color = rgbToHex(pending.red, pending.green, pending.blue);
        colorTriggerInput.value = color.toLowerCase();
        colorWheel.setColor(color);
        colorTriggerValue.value = color;
        colorTriggerValue.classList.remove("is-invalid");
        colorTriggerDurationInput.value = String(pending.duration);
    }

    function renderPalette() {
        const pageCount = Math.max(1, Math.ceil(objectButtons.length / PALETTE_PAGE_SIZE));
        state.palettePage = (state.palettePage % pageCount + pageCount) % pageCount;
        const firstIndex = state.palettePage * PALETTE_PAGE_SIZE;
        const lastIndex = firstIndex + PALETTE_PAGE_SIZE;

        objectButtons.forEach((button, index) => {
            button.hidden = index < firstIndex || index >= lastIndex;
        });

        previousPaletteButton.disabled = pageCount === 1;
        nextPaletteButton.disabled = pageCount === 1;
        palettePages.textContent = `${state.palettePage + 1} / ${pageCount}`;
    }

    function setEditorTab(tabName) {
        state.editorTab = tabName;

        for (const button of editorTabButtons) {
            const isActive = button.dataset.editorTab === tabName;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-pressed", String(isActive));
        }

        for (const panel of editorPanels) {
            panel.hidden = panel.dataset.editorPanel !== tabName;
        }

        palettePages.hidden = tabName !== "build";
    }

    function onEditorTabClick(event) {
        setEditorTab(event.currentTarget.dataset.editorTab);
    }

    function selectObject(button) {
        state.selectedObjectType = button.dataset.objectType;
        state.selectedRotation = 0;

        if (state.pendingObject) {
            state.pendingObject = createPendingObject(
                state.selectedObjectType,
                state.pendingObject.x,
                state.pendingObject.y,
                0);
        }

        for (const objectButton of objectButtons) {
            objectButton.classList.toggle("is-selected", objectButton === button);
        }

        updateToolStatus();
        updatePlacementControls();
        setEditorTab("build");
        requestDraw();
    }

    function preparePlacement(cell) {
        state.pendingObject = createPendingObject(
            state.selectedObjectType,
            cell.x,
            cell.y,
            state.selectedRotation);
        state.selectedCell = { x: cell.x, y: cell.y };

        setEditorTab("edit");
        updatePlacementControls();
        updateStatus();
        requestDraw();
    }

    function movePendingObject(deltaX, deltaY) {
        if (!state.pendingObject) {
            return;
        }

        const nextX = Math.min(Math.max(state.pendingObject.x + deltaX, 0), COLUMN_COUNT - 1);
        const nextY = Math.min(Math.max(state.pendingObject.y + deltaY, 0), ROW_COUNT - 1);
        state.pendingObject.x = nextX;
        state.pendingObject.y = nextY;
        state.selectedCell = { x: nextX, y: nextY };
        updatePlacementControls();
        updateStatus();
        requestDraw();
    }

    function onMoveButtonClick(event) {
        movePendingObject(
            Number(event.currentTarget.dataset.moveX),
            Number(event.currentTarget.dataset.moveY));
    }

    function confirmPlacement() {
        if (!state.pendingObject) {
            return;
        }

        const confirmedObject = createConfirmedObject(state.pendingObject);
        state.objects.set(cellKey(confirmedObject), confirmedObject);
        root.dispatchEvent(new CustomEvent("editor:object-placed", {
            detail: { ...confirmedObject }
        }));
        state.pendingObject = null;
        state.selectedCell = null;
        setEditorTab("build");
        updatePlacementControls();
        updateStatus();
        requestDraw();
    }

    function rotateObject(event) {
        if (!state.pendingObject || !selectedObjectCanRotate()) {
            return;
        }

        const step = Number(event.currentTarget.dataset.rotateStep ?? 90);
        state.selectedRotation = (state.selectedRotation + step + 360) % 360;

        if (state.pendingObject) {
            state.pendingObject.rotation = state.selectedRotation;
        }

        updateToolStatus();
        requestDraw();
    }

    function onObjectButtonClick(event) {
        selectObject(event.currentTarget);
    }

    function onColorTriggerTargetClick(event) {
        if (state.pendingObject?.type !== COLOR_TRIGGER_TYPE) {
            return;
        }

        state.pendingObject.colorTarget = event.currentTarget.dataset.colorTriggerTarget;
        updateColorTriggerSettings();
    }

    function onColorTriggerInput(event) {
        if (state.pendingObject?.type !== COLOR_TRIGGER_TYPE) {
            return;
        }

        Object.assign(state.pendingObject, hexToRgb(event.currentTarget.value));
        updateColorTriggerSettings();
    }

    function normalizedHexColor(value) {
        const match = /^#?([0-9a-f]{6})$/i.exec(value.trim());
        return match ? `#${match[1].toUpperCase()}` : null;
    }

    function onColorTriggerHexInput(event) {
        const hexColor = normalizedHexColor(event.currentTarget.value);
        event.currentTarget.classList.toggle("is-invalid", !hexColor);

        if (!hexColor || state.pendingObject?.type !== COLOR_TRIGGER_TYPE) {
            return;
        }

        colorTriggerInput.value = hexColor;
        colorTriggerInput.dispatchEvent(new Event("input", { bubbles: true }));
    }

    function onColorTriggerHexBlur() {
        updateColorTriggerSettings();
    }

    function onColorTriggerHexKeyDown(event) {
        if (event.key === "Enter") {
            event.currentTarget.blur();
            event.preventDefault();
        }
    }

    function onColorTriggerDurationInput(event) {
        if (state.pendingObject?.type !== COLOR_TRIGGER_TYPE) {
            return;
        }

        const duration = Number.parseFloat(event.currentTarget.value);
        if (Number.isFinite(duration)) {
            state.pendingObject.duration = Math.max(0, duration);
        }
    }

    function showPreviousPalettePage() {
        state.palettePage -= 1;
        renderPalette();
    }

    function showNextPalettePage() {
        state.palettePage += 1;
        renderPalette();
    }

    function setZoom(nextZoom, anchorX = state.width / 2) {
        const previousSize = cellSize();
        const worldX = state.offsetX + anchorX / previousSize;
        const groundScreenY = gridToScreenY(0);

        state.zoom = Math.min(Math.max(nextZoom, MIN_ZOOM), MAX_ZOOM);

        const nextSize = cellSize();
        state.offsetX = worldX - anchorX / nextSize;
        state.offsetY = (groundScreenY - groundBaseline()) / nextSize;
        clampCamera();
        updateStatus();
        updateTimeline();
        requestDraw();
    }

    function resize() {
        const bounds = canvas.getBoundingClientRect();
        const devicePixelRatio = window.devicePixelRatio || 1;
        state.width = Math.max(1, bounds.width);
        state.height = Math.max(1, bounds.height);
        state.baseCellSize = state.height / (ROW_COUNT + GROUND_TILE_CELLS);

        canvas.width = Math.round(state.width * devicePixelRatio);
        canvas.height = Math.round(state.height * devicePixelRatio);
        context.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0);

        clampCamera();
        draw();
        updateTimeline();
    }

    function pointerPosition(event) {
        const bounds = canvas.getBoundingClientRect();
        return {
            x: event.clientX - bounds.left,
            y: event.clientY - bounds.top
        };
    }

    function onPointerDown(event) {
        if (event.button !== 0 && event.button !== 1) {
            return;
        }

        const position = pointerPosition(event);
        state.pointerId = event.pointerId;
        state.pointerX = position.x;
        state.pointerY = position.y;
        state.dragDistance = 0;
        canvas.setPointerCapture(event.pointerId);
        event.preventDefault();
    }

    function onPointerMove(event) {
        const position = pointerPosition(event);

        if (state.pointerId === event.pointerId) {
            const deltaX = position.x - state.pointerX;
            const deltaY = position.y - state.pointerY;
            state.pointerX = position.x;
            state.pointerY = position.y;
            state.dragDistance += Math.abs(deltaX) + Math.abs(deltaY);
            state.offsetX -= deltaX / cellSize();
            state.offsetY += deltaY / cellSize();
            clampCamera();
            updateTimeline();
        }

        state.hoverCell = screenToCell(position.x, position.y);
        updateStatus();
        requestDraw();
    }

    function finishPointer(event) {
        if (state.pointerId !== event.pointerId) {
            return;
        }

        if (state.dragDistance < 5) {
            const position = pointerPosition(event);
            state.selectedCell = screenToCell(position.x, position.y);

            if (state.selectedCell) {
                preparePlacement(state.selectedCell);
            }
        }

        state.pointerId = null;
        updateStatus();
        requestDraw();
    }

    function onPointerLeave() {
        if (state.pointerId === null) {
            state.hoverCell = null;
            updateStatus();
            requestDraw();
        }
    }

    function onWheel(event) {
        if (event.ctrlKey) {
            return;
        }

        event.preventDefault();
        const position = pointerPosition(event);
        const factor = Math.exp(-event.deltaY * 0.0015);
        setZoom(state.zoom * factor, position.x);
    }

    function zoomIn() {
        setZoom(state.zoom * 1.2);
    }

    function zoomOut() {
        setZoom(state.zoom / 1.2);
    }

    function onTimelinePointerDown(event) {
        if (event.button !== 0) {
            return;
        }

        state.timelinePointerId = event.pointerId;
        state.timelinePointerX = event.clientX;
        state.timelineStartProgress = cameraProgress();
        timelineHandle.setPointerCapture(event.pointerId);
        event.preventDefault();
    }

    function onTimelinePointerMove(event) {
        if (state.timelinePointerId !== event.pointerId) {
            return;
        }

        const bounds = timeline.getBoundingClientRect();
        const progressDelta = (event.clientX - state.timelinePointerX) / bounds.width;
        setTimelineProgress(state.timelineStartProgress + progressDelta);
        event.preventDefault();
    }

    function finishTimelinePointer(event) {
        if (state.timelinePointerId === event.pointerId) {
            state.timelinePointerId = null;
        }
    }

    function onTimelineKeyDown(event) {
        const currentProgress = cameraProgress();
        const tenColumnStep = 10 / Math.max(1, horizontalTravelCells());

        switch (event.key) {
            case "ArrowLeft":
                setTimelineProgress(currentProgress - tenColumnStep);
                break;
            case "ArrowRight":
                setTimelineProgress(currentProgress + tenColumnStep);
                break;
            case "PageUp":
                setTimelineProgress(currentProgress - 0.1);
                break;
            case "PageDown":
                setTimelineProgress(currentProgress + 0.1);
                break;
            case "Home":
                setTimelineProgress(0);
                break;
            case "End":
                setTimelineProgress(1);
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(root);
    window.addEventListener("resize", resize);
    canvas.addEventListener("pointerdown", onPointerDown);
    canvas.addEventListener("pointermove", onPointerMove);
    canvas.addEventListener("pointerup", finishPointer);
    canvas.addEventListener("pointercancel", finishPointer);
    canvas.addEventListener("pointerleave", onPointerLeave);
    canvas.addEventListener("wheel", onWheel, { passive: false });
    zoomInButton.addEventListener("click", zoomIn);
    zoomOutButton.addEventListener("click", zoomOut);
    objectButtons.forEach(button => button.addEventListener("click", onObjectButtonClick));
    editorTabButtons.forEach(button => button.addEventListener("click", onEditorTabClick));
    previousPaletteButton.addEventListener("click", showPreviousPalettePage);
    nextPaletteButton.addEventListener("click", showNextPalettePage);
    rotateObjectButtons.forEach(button => button.addEventListener("click", rotateObject));
    movementButtons.forEach(button => button.addEventListener("click", onMoveButtonClick));
    confirmPlacementButton.addEventListener("click", confirmPlacement);
    colorTriggerTargetButtons.forEach(button => button.addEventListener("click", onColorTriggerTargetClick));
    colorTriggerInput.addEventListener("input", onColorTriggerInput);
    colorTriggerValue.addEventListener("input", onColorTriggerHexInput);
    colorTriggerValue.addEventListener("blur", onColorTriggerHexBlur);
    colorTriggerValue.addEventListener("keydown", onColorTriggerHexKeyDown);
    colorTriggerDurationInput.addEventListener("input", onColorTriggerDurationInput);
    timelineHandle.addEventListener("pointerdown", onTimelinePointerDown);
    timelineHandle.addEventListener("pointermove", onTimelinePointerMove);
    timelineHandle.addEventListener("pointerup", finishTimelinePointer);
    timelineHandle.addEventListener("pointercancel", finishTimelinePointer);
    timeline.addEventListener("keydown", onTimelineKeyDown);
    renderPalette();
    setEditorTab("build");
    updateToolStatus();
    updatePlacementControls();
    resize();
    updateStatus();

    editorInstances.set(root, {
        dispose() {
            resizeObserver.disconnect();
            window.removeEventListener("resize", resize);
            canvas.removeEventListener("pointerdown", onPointerDown);
            canvas.removeEventListener("pointermove", onPointerMove);
            canvas.removeEventListener("pointerup", finishPointer);
            canvas.removeEventListener("pointercancel", finishPointer);
            canvas.removeEventListener("pointerleave", onPointerLeave);
            canvas.removeEventListener("wheel", onWheel);
            zoomInButton.removeEventListener("click", zoomIn);
            zoomOutButton.removeEventListener("click", zoomOut);
            objectButtons.forEach(button => button.removeEventListener("click", onObjectButtonClick));
            editorTabButtons.forEach(button => button.removeEventListener("click", onEditorTabClick));
            previousPaletteButton.removeEventListener("click", showPreviousPalettePage);
            nextPaletteButton.removeEventListener("click", showNextPalettePage);
            rotateObjectButtons.forEach(button => button.removeEventListener("click", rotateObject));
            movementButtons.forEach(button => button.removeEventListener("click", onMoveButtonClick));
            confirmPlacementButton.removeEventListener("click", confirmPlacement);
            colorTriggerTargetButtons.forEach(button => button.removeEventListener("click", onColorTriggerTargetClick));
            colorTriggerInput.removeEventListener("input", onColorTriggerInput);
            colorTriggerValue.removeEventListener("input", onColorTriggerHexInput);
            colorTriggerValue.removeEventListener("blur", onColorTriggerHexBlur);
            colorTriggerValue.removeEventListener("keydown", onColorTriggerHexKeyDown);
            colorTriggerDurationInput.removeEventListener("input", onColorTriggerDurationInput);
            colorWheel.dispose();
            timelineHandle.removeEventListener("pointerdown", onTimelinePointerDown);
            timelineHandle.removeEventListener("pointermove", onTimelinePointerMove);
            timelineHandle.removeEventListener("pointerup", finishTimelinePointer);
            timelineHandle.removeEventListener("pointercancel", finishTimelinePointer);
            timeline.removeEventListener("keydown", onTimelineKeyDown);

            if (state.frame) {
                cancelAnimationFrame(state.frame);
            }
        }
    });
}

export function disposeEditorGrid(root) {
    const instance = editorInstances.get(root);
    instance?.dispose();
    editorInstances.delete(root);
}

function initializeCurrentPage() {
    const root = document.querySelector(".editor-page");
    if (root) {
        initializeEditorGrid(root);
    }
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeCurrentPage, { once: true });
} else {
    initializeCurrentPage();
}

window.addEventListener("pageshow", initializeCurrentPage);
