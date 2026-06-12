using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;
using Xunit;

namespace UnityCli.Cli.Tests;

public class QaSequenceSpecParserTests
{
    [Fact]
    public void Parse_TransformNearCondition_NormalizesVectorAndOp()
    {
        const string json = """
        { "steps": [ { "name": "move",
          "wait": [ { "target": "/Battle/Erich", "transform": "position", "op": "near", "value": [3,0,3], "epsilon": 0.1 } ],
          "timeoutMs": 5000,
          "actions": [ { "key": "right" }, { "key": "space" } ] } ] }
        """;

        QaRunSequenceArgs args = QaSequenceSpecParser.Parse(json);

        var cond = args.steps[0].wait[0];
        Assert.Equal("transform", cond.kind);
        Assert.Equal("position", cond.key);
        Assert.Equal("near", cond.op);
        Assert.Equal("3,0,3", cond.value);
        Assert.Equal(0.1f, cond.epsilon, 3);
        Assert.Equal(5000, args.steps[0].timeoutMs);
        Assert.Equal("key", args.steps[0].actions[0].kind);
        Assert.Equal("right", args.steps[0].actions[0].key);
    }

    [Fact]
    public void Parse_ActiveCondition_SetsBoolValue()
    {
        const string json = """
        { "steps": [ { "wait": [ { "target": "/UI/UICommand", "active": true } ],
          "actions": [ { "key": "down" } ] } ] }
        """;
        var cond = QaSequenceSpecParser.Parse(json).steps[0].wait[0];
        Assert.Equal("active", cond.kind);
        Assert.Equal("true", cond.value);
    }

    [Fact]
    public void Parse_QueryEquals_NormalizesStringValue()
    {
        const string json = """
        { "steps": [ { "wait": [ { "target": "/Battle/BattleState", "query": "phase", "op": "==", "value": "PlayerTurn" } ],
          "actions": [ { "key": "space" } ] } ] }
        """;
        var cond = QaSequenceSpecParser.Parse(json).steps[0].wait[0];
        Assert.Equal("query", cond.kind);
        Assert.Equal("phase", cond.key);
        Assert.Equal("==", cond.op);
        Assert.Equal("PlayerTurn", cond.value);
    }

    [Fact]
    public void Parse_TapWithTargetAction()
    {
        const string json = """
        { "steps": [ { "wait": [ { "target": "/X", "active": true } ],
          "actions": [ { "tap": { "target": "/Battle/Erich" } } ] } ] }
        """;
        var action = QaSequenceSpecParser.Parse(json).steps[0].actions[0];
        Assert.Equal("tap", action.kind);
        Assert.False(action.hasTapCoords);
        Assert.Equal("/Battle/Erich", action.target);
    }

    [Fact]
    public void Parse_EmptySteps_Throws()
        => Assert.Throws<CliUsageException>(() => QaSequenceSpecParser.Parse("""{ "steps": [] }"""));

    [Fact]
    public void Parse_UnknownOp_Throws()
        => Assert.Throws<CliUsageException>(() => QaSequenceSpecParser.Parse("""
        { "steps": [ { "wait": [ { "target": "/X", "query": "hp", "op": "~~", "value": "0" } ], "actions": [ { "key": "a" } ] } ] }
        """));
}
