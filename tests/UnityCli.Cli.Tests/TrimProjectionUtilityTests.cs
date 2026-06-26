using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class TrimProjectionUtilityTests
{
    [Fact]
    public void ApplyUiDumpFilters_DefaultArgsPreserveElementsAndOrder()
    {
        QaUiElement[] elements =
        [
            Ui("Top", interactable: false),
            Ui("Bottom", interactable: true),
        ];

        QaUiElement[] result = QaDumpProjectionUtility.ApplyUiDumpFilters(elements, new QaUiDumpArgs());

        Assert.Same(elements[0], result[0]);
        Assert.Same(elements[1], result[1]);
    }

    [Fact]
    public void ApplyUiDumpFilters_AppliesInteractableTextAndLimitAfterCurrentOrder()
    {
        QaUiElement[] elements =
        [
            Ui("Start Game", interactable: true),
            Ui("Start Disabled", interactable: false),
            Ui("Start Options", interactable: true),
            Ui("Quit", interactable: true),
        ];

        QaUiElement[] result = QaDumpProjectionUtility.ApplyUiDumpFilters(
            elements,
            new QaUiDumpArgs
            {
                interactableOnly = true,
                text = "start",
                limit = 1,
            });

        Assert.Single(result);
        Assert.Equal("Start Game", result[0].text);
    }

    [Fact]
    public void ApplyWorldDumpFilters_DefaultArgsPreserveElementsAndOrder()
    {
        QaWorldElement[] elements =
        [
            World("Enemy A", onScreen: true, hasAction: true),
            World("Enemy B", onScreen: false, hasAction: false),
        ];

        QaWorldElement[] result = QaDumpProjectionUtility.ApplyWorldDumpFilters(elements, new QaWorldDumpArgs());

        Assert.Same(elements[0], result[0]);
        Assert.Same(elements[1], result[1]);
    }

    [Fact]
    public void ApplyWorldDumpFilters_AppliesTextAndLimitAfterCurrentOrder()
    {
        QaWorldElement[] elements =
        [
            World("Enemy A", onScreen: true, hasAction: true),
            World("Enemy B", onScreen: true, hasAction: true),
            World("Chest", onScreen: true, hasAction: true),
        ];

        QaWorldElement[] result = QaDumpProjectionUtility.ApplyWorldDumpFilters(
            elements,
            new QaWorldDumpArgs
            {
                text = "enemy",
                limit = 1,
            });

        Assert.Single(result);
        Assert.Equal("Enemy A", result[0].label);
    }

    [Fact]
    public void WorldProjection_OnlyIncludesOnScreenWhenIncludeOffscreenHasMixedValues()
    {
        QaWorldElement[] elements =
        [
            World("Visible", onScreen: true, hasAction: true),
            World("Hidden", onScreen: false, hasAction: true),
        ];

        Assert.False(QaDumpProjectionUtility.ShouldIncludeWorldOnScreenField(elements, new QaWorldDumpArgs()));
        Assert.True(QaDumpProjectionUtility.ShouldIncludeWorldOnScreenField(
            elements,
            new QaWorldDumpArgs { includeOffscreen = true }));
    }

    [Fact]
    public void WorldProjection_OmitsHasActionWhenAllEntriesAreActionable()
    {
        QaWorldElement[] actionable =
        [
            World("Visible", onScreen: true, hasAction: true),
            World("Also Visible", onScreen: true, hasAction: true),
        ];
        QaWorldElement[] mixed =
        [
            World("Visible", onScreen: true, hasAction: true),
            World("Marker Only", onScreen: true, hasAction: false),
        ];

        Assert.False(QaDumpProjectionUtility.ShouldIncludeWorldHasActionField(actionable));
        Assert.True(QaDumpProjectionUtility.ShouldIncludeWorldHasActionField(mixed));
    }

    [Fact]
    public void ApplyFailuresOnly_PreservesSummaryAndWarnings()
    {
        var summary = new TestRunSummary { total = 3, passed = 1, failed = 1, skipped = 1, completed = 3 };
        var payload = new TestRunResultPayload
        {
            runId = "run-1",
            mode = "edit",
            status = "Completed",
            summary = summary,
            tests =
            [
                new TestResultEntry { fullName = "PassedTest", outcome = "Passed" },
                new TestResultEntry { fullName = "FailedTest", outcome = "Failed" },
                new TestResultEntry { fullName = "SkippedTest", outcome = "Skipped" },
            ],
            warnings = ["warning"],
        };

        TestRunResultPayload result = TestResultProjectionUtility.ApplyFailuresOnly(payload, failuresOnly: true);

        Assert.Same(summary, result.summary);
        Assert.Equal(2, result.tests.Length);
        Assert.DoesNotContain(result.tests, test => test.outcome == "Passed");
        Assert.Equal(["warning"], result.warnings);
    }

    [Fact]
    public void ApplyFailuresOnly_DefaultReturnsSamePayload()
    {
        var payload = new TestRunResultPayload();

        TestRunResultPayload result = TestResultProjectionUtility.ApplyFailuresOnly(payload, failuresOnly: false);

        Assert.Same(payload, result);
    }

    [Fact]
    public void ConsoleProjection_DefaultKeepsStackTrace()
    {
        Assert.False(ConsoleLogProjectionUtility.ShouldOmitStackTrace(new ReadConsoleArgs()));
        Assert.True(ConsoleLogProjectionUtility.ShouldOmitStackTrace(new ReadConsoleArgs { noStackTrace = true }));
    }

    private static QaUiElement Ui(string text, bool interactable)
    {
        return new QaUiElement
        {
            path = "/" + text.Replace(" ", string.Empty, StringComparison.Ordinal),
            text = text,
            interactable = interactable,
        };
    }

    private static QaWorldElement World(string label, bool onScreen, bool hasAction)
    {
        return new QaWorldElement
        {
            path = "/" + label.Replace(" ", string.Empty, StringComparison.Ordinal),
            label = label,
            onScreen = onScreen,
            hasAction = hasAction,
        };
    }
}
