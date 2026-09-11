export function loadSceneTexture(path, onLoad) {
    const image = new Image();
    image.addEventListener("load", onLoad);
    image.src = path;
    return image;
}

function isReady(image) {
    return image?.complete && image.naturalWidth > 0 && image.naturalHeight > 0;
}

function wrappedOffset(offset, tileWidth) {
    return ((offset % tileWidth) + tileWidth) % tileWidth;
}

function drawRepeatedTexture(context, image, width, top, height, offsetX) {
    if (!isReady(image) || width <= 0 || height <= 0) {
        return;
    }

    const scale = height / image.naturalHeight;
    const tileWidth = image.naturalWidth * scale;
    const firstX = wrappedOffset(offsetX, tileWidth) - tileWidth;

    for (let x = firstX; x < width; x += tileWidth) {
        context.drawImage(image, x, top, tileWidth, height);
    }
}

function channel(value) {
    return Math.round(Math.min(255, Math.max(0, value)));
}

function rgb(color, redFactor = 1, greenFactor = 1, blueFactor = 1) {
    return `rgb(${channel(color.red * redFactor)} ${channel(color.green * greenFactor)} ${channel(color.blue * blueFactor)})`;
}

export function drawClassicScene(context, options) {
    const {
        width,
        height,
        groundTop,
        groundTileSize,
        worldOffsetPixels,
        backgroundImage,
        groundImage,
        backgroundColor,
        groundColor
    } = options;

    const backgroundGradient = context.createLinearGradient(0, 0, 0, groundTop);
    backgroundGradient.addColorStop(0, rgb(backgroundColor, 0.84, 0.658, 0.705));
    backgroundGradient.addColorStop(0.55, rgb(backgroundColor));
    backgroundGradient.addColorStop(1, rgb(backgroundColor, 0.88, 1.162, 1.1));
    context.fillStyle = backgroundGradient;
    context.fillRect(0, 0, width, groundTop);

    context.save();
    context.beginPath();
    context.rect(0, 0, width, groundTop);
    context.clip();
    context.globalAlpha = 0.62;
    context.globalCompositeOperation = "multiply";
    drawRepeatedTexture(
        context,
        backgroundImage,
        width,
        0,
        groundTop,
        -worldOffsetPixels * 0.16);
    context.restore();

    const groundHeight = Math.max(0, height - groundTop);
    const groundGradient = context.createLinearGradient(
        0,
        groundTop,
        0,
        groundTop + groundTileSize);
    groundGradient.addColorStop(0, rgb(groundColor));
    groundGradient.addColorStop(1, rgb(groundColor, 0, 0.484, 0.616));
    context.fillStyle = groundGradient;
    context.fillRect(0, groundTop, width, groundHeight);

    context.save();
    context.beginPath();
    context.rect(0, groundTop, width, groundHeight);
    context.clip();
    context.globalAlpha = 0.78;
    context.globalCompositeOperation = "multiply";
    drawRepeatedTexture(
        context,
        groundImage,
        width,
        groundTop,
        groundTileSize,
        -worldOffsetPixels);
    context.restore();

    const boundaryGradient = context.createLinearGradient(0, groundTop - 3, 0, groundTop + 4);
    boundaryGradient.addColorStop(0, "rgba(103, 241, 255, 0)");
    boundaryGradient.addColorStop(0.42, rgb(groundColor, 22.8, 1.94, 1));
    boundaryGradient.addColorStop(1, rgb(groundColor, 1.2, 1.33, 1));
    context.fillStyle = boundaryGradient;
    context.fillRect(0, groundTop - 3, width, 7);
}
