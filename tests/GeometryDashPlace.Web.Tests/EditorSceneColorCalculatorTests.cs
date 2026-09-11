using GeometryDashPlace.Web.Components.Editor.State;

namespace GeometryDashPlace.Web.Tests;

public sealed class EditorSceneColorCalculatorTests
{
    [Fact]
    public void NormalSpeed_ProducesExactHalfwayColorAfterHalfTheFadeTime()
    {
        var triggerX = 5;
        var objects = new[]
        {
            ColorTrigger("bg_color_trigger", 0, 255, 255, 255, 0),
            ColorTrigger("bg_color_trigger", triggerX, 128, 0, 255, 5)
        };
        var halfwayX = triggerX + 0.5 +
            EditorSceneColorCalculator.NormalSpeedCellsPerSecond * 2.5;

        var halfway = EditorSceneColorCalculator.Calculate(objects, halfwayX).Background;
        var samePositionAgain = EditorSceneColorCalculator.Calculate(objects, halfwayX).Background;

        Assert.NotNull(halfway);
        Assert.Equal(191.5, halfway.Red, 6);
        Assert.Equal(127.5, halfway.Green, 6);
        Assert.Equal(255, halfway.Blue, 6);
        Assert.Equal(halfway, samePositionAgain);
    }

    [Fact]
    public void SpeedPortals_AreIncludedInTheElapsedTravelTime()
    {
        var triggerX = 5;
        var speedPortalX = 10;
        var objects = new[]
        {
            ColorTrigger("bg_color_trigger", 0, 255, 255, 255, 0),
            ColorTrigger("bg_color_trigger", triggerX, 0, 0, 255, 5),
            new EditorObjectInstance { Type = "fast_speed", X = speedPortalX, Y = 0 }
        };
        var centerX = speedPortalX + 0.5 +
            EditorSceneColorCalculator.FastSpeedCellsPerSecond * 1.5;
        var elapsedSinceTrigger =
            (speedPortalX - triggerX) / EditorSceneColorCalculator.NormalSpeedCellsPerSecond + 1.5;
        var expectedProgress = elapsedSinceTrigger / 5;

        var color = EditorSceneColorCalculator.Calculate(objects, centerX).Background;

        Assert.NotNull(color);
        Assert.Equal(255 * (1 - expectedProgress), color.Red, 6);
        Assert.Equal(255 * (1 - expectedProgress), color.Green, 6);
        Assert.Equal(255, color.Blue, 6);
    }

    [Fact]
    public void ShorterTrigger_OverridesThenLongerTriggerResumesUntilItsOriginalEndTime()
    {
        const int longTriggerX = 1;
        const int shortTriggerX = 301;
        const double longDuration = 100;
        const double shortDuration = 10;
        var objects = new[]
        {
            ColorTrigger("bg_color_trigger", 0, 255, 255, 255, 0),
            ColorTrigger("bg_color_trigger", longTriggerX, 255, 0, 0, longDuration),
            ColorTrigger("bg_color_trigger", shortTriggerX, 0, 255, 0, shortDuration)
        };
        var shortTriggerStartsAfter =
            (shortTriggerX - longTriggerX) /
            EditorSceneColorCalculator.NormalSpeedCellsPerSecond;
        var remainingLongFade = longDuration - shortTriggerStartsAfter - shortDuration;
        var shortTriggerEndX = shortTriggerX + 0.5 +
            EditorSceneColorCalculator.NormalSpeedCellsPerSecond * shortDuration;
        var halfwayThroughResumedFadeX = shortTriggerEndX +
            EditorSceneColorCalculator.NormalSpeedCellsPerSecond * remainingLongFade / 2;
        var originalLongTriggerEndX = longTriggerX + 0.5 +
            EditorSceneColorCalculator.NormalSpeedCellsPerSecond * longDuration;

        var whenShortTriggerFinishes =
            EditorSceneColorCalculator.Calculate(objects, shortTriggerEndX).Background;
        var halfwayThroughResumedFade =
            EditorSceneColorCalculator.Calculate(objects, halfwayThroughResumedFadeX).Background;
        var whenOriginalTriggerFinishes =
            EditorSceneColorCalculator.Calculate(objects, originalLongTriggerEndX).Background;

        Assert.NotNull(whenShortTriggerFinishes);
        Assert.Equal(0, whenShortTriggerFinishes.Red, 6);
        Assert.Equal(255, whenShortTriggerFinishes.Green, 6);
        Assert.Equal(0, whenShortTriggerFinishes.Blue, 6);

        Assert.NotNull(halfwayThroughResumedFade);
        Assert.Equal(127.5, halfwayThroughResumedFade.Red, 6);
        Assert.Equal(127.5, halfwayThroughResumedFade.Green, 6);
        Assert.Equal(0, halfwayThroughResumedFade.Blue, 6);
        Assert.Equal(longTriggerX, halfwayThroughResumedFade.TriggerX);

        Assert.NotNull(whenOriginalTriggerFinishes);
        Assert.Equal(255, whenOriginalTriggerFinishes.Red, 6);
        Assert.Equal(0, whenOriginalTriggerFinishes.Green, 6);
        Assert.Equal(0, whenOriginalTriggerFinishes.Blue, 6);
    }

    private static EditorObjectInstance ColorTrigger(
        string type,
        int x,
        int red,
        int green,
        int blue,
        double duration) => new()
        {
            Type = type,
            X = x,
            Y = 1,
            Red = red,
            Green = green,
            Blue = blue,
            Duration = duration
        };
}
