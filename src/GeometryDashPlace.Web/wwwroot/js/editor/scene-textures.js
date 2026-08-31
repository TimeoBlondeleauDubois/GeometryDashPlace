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

export function drawClassicScene(context, options) {
    const {
        width,
        height,
        groundTop,
        groundTileSize,
        worldOffsetPixels,
        backgroundImage,
        groundImage
    } = options;

    const backgroundGradient = context.createLinearGradient(0, 0, 0, groundTop);
    backgroundGradient.addColorStop(0, "#154d9b");
    backgroundGradient.addColorStop(0.55, "#1975dc");
    backgroundGradient.addColorStop(1, "#1688f2");
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
    groundGradient.addColorStop(0, "#057eff");
    groundGradient.addColorStop(1, "#003d9d");
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
    boundaryGradient.addColorStop(0.42, "#72f5ff");
    boundaryGradient.addColorStop(1, "#06a8ff");
    context.fillStyle = boundaryGradient;
    context.fillRect(0, groundTop - 3, width, 7);
}
