const COLUMN_COUNT = 1024;
const ROW_COUNT = 32;
const MIN_ZOOM = 0.5;
const MAX_ZOOM = 3;
const editorInstances = new WeakMap();

export function initializeEditorGrid(root) {
    if (editorInstances.has(root)) {
        return;
    }

    const canvas = root.querySelector("canvas");
    const context = canvas.getContext("2d", { alpha: false });
    const coordinateValue = root.querySelector("[data-coordinate-value]");
    const zoomValue = root.querySelector("[data-zoom-value]");
    const hint = root.querySelector("[data-editor-hint]");
    const zoomInButton = root.querySelector('[data-editor-action="zoom-in"]');
    const zoomOutButton = root.querySelector('[data-editor-action="zoom-out"]');
    const timeline = root.querySelector("[data-editor-timeline]");
    const timelineHandle = root.querySelector("[data-timeline-handle]");

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
        timelinePointerId: null,
        timelinePointerX: 0,
        timelineStartProgress: 0,
        frame: 0
    };

    function cellSize() {
        return state.baseCellSize * state.zoom;
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
        state.offsetY = axisOffset(state.offsetY, ROW_COUNT, state.height / size);
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
        return state.height - (y - state.offsetY) * cellSize();
    }

    function screenToCell(x, y) {
        const size = cellSize();
        const column = Math.floor(state.offsetX + x / size);
        const row = Math.floor(state.offsetY + (state.height - y) / size);

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

    function draw() {
        state.frame = 0;
        const size = cellSize();

        context.clearRect(0, 0, state.width, state.height);
        context.fillStyle = "#0a1930";
        context.fillRect(0, 0, state.width, state.height);

        const gridLeft = gridToScreenX(0);
        const gridRight = gridToScreenX(COLUMN_COUNT);
        const gridTop = gridToScreenY(ROW_COUNT);
        const gridBottom = gridToScreenY(0);
        const visibleLeft = Math.max(0, gridLeft);
        const visibleRight = Math.min(state.width, gridRight);
        const visibleTop = Math.max(0, gridTop);
        const visibleBottom = Math.min(state.height, gridBottom);

        if (visibleRight > visibleLeft && visibleBottom > visibleTop) {
            const blueGradient = context.createLinearGradient(0, visibleTop, 0, visibleBottom);
            blueGradient.addColorStop(0, "#215ca8");
            blueGradient.addColorStop(0.55, "#287bd8");
            blueGradient.addColorStop(1, "#2074cf");
            context.fillStyle = blueGradient;
            context.fillRect(
                visibleLeft,
                visibleTop,
                visibleRight - visibleLeft,
                visibleBottom - visibleTop);
        }

        drawCell(state.hoverCell, "rgba(111, 196, 255, 0.25)", "rgba(174, 228, 255, 0.85)", 2);
        drawCell(state.selectedCell, "rgba(255, 225, 55, 0.28)", "#fff36a", 3);

        const firstColumn = Math.max(0, Math.floor(state.offsetX));
        const lastColumn = Math.min(COLUMN_COUNT, Math.ceil(state.offsetX + state.width / size));
        const firstRow = Math.max(0, Math.floor(state.offsetY));
        const lastRow = Math.min(ROW_COUNT, Math.ceil(state.offsetY + state.height / size));

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
    }

    function setZoom(nextZoom, anchorX = state.width / 2, anchorY = state.height / 2) {
        const previousSize = cellSize();
        const worldX = state.offsetX + anchorX / previousSize;
        const worldY = state.offsetY + (state.height - anchorY) / previousSize;

        state.zoom = Math.min(Math.max(nextZoom, MIN_ZOOM), MAX_ZOOM);

        const nextSize = cellSize();
        state.offsetX = worldX - anchorX / nextSize;
        state.offsetY = worldY - (state.height - anchorY) / nextSize;
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
        state.baseCellSize = state.height / ROW_COUNT;

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
        hint.classList.add("is-hidden");
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
        setZoom(state.zoom * factor, position.x, position.y);
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
        hint.classList.add("is-hidden");
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
    timelineHandle.addEventListener("pointerdown", onTimelinePointerDown);
    timelineHandle.addEventListener("pointermove", onTimelinePointerMove);
    timelineHandle.addEventListener("pointerup", finishTimelinePointer);
    timelineHandle.addEventListener("pointercancel", finishTimelinePointer);
    timeline.addEventListener("keydown", onTimelineKeyDown);
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
