using UnityCli.Protocol;
using Xunit;

namespace UnityCli.Cli.Tests;

public class QaConditionOpsTests
{
    [Theory]
    [InlineData("5", "5", true)]
    [InlineData("5", "6", false)]
    [InlineData("3,0,3", "3,0,3", true)]
    [InlineData("3,0,3", "3,0,4", false)]
    public void Equals_ComparesNumbersWithinEpsilon(string actual, string expected, bool result)
        => Assert.Equal(result, QaConditionOps.Evaluate(actual, "==", expected, 0f));

    [Theory]
    [InlineData("3.04,0,3", "3,0,3", 0.1f, true)]
    [InlineData("3.2,0,3", "3,0,3", 0.1f, false)]
    public void Near_UsesEpsilonPerComponent(string actual, string expected, float eps, bool result)
        => Assert.Equal(result, QaConditionOps.Evaluate(actual, "near", expected, eps));

    [Theory]
    [InlineData("7", ">=", "5", true)]
    [InlineData("3", ">=", "5", false)]
    [InlineData("3", "<=", "5", true)]
    public void Relational_ScalarOnly(string actual, string op, string expected, bool result)
        => Assert.Equal(result, QaConditionOps.Evaluate(actual, op, expected, 0f));

    [Theory]
    [InlineData("PlayerTurn", "==", "PlayerTurn", true)]
    [InlineData("EnemyTurn", "==", "PlayerTurn", false)]
    [InlineData("EnemyTurn", "!=", "PlayerTurn", true)]
    public void StringEquality_WhenNotNumeric(string actual, string op, string expected, bool result)
        => Assert.Equal(result, QaConditionOps.Evaluate(actual, op, expected, 0f));

    [Theory]
    [InlineData("true", "==", "true", true)]
    [InlineData("false", "==", "true", false)]
    public void BoolEquality(string actual, string op, string expected, bool result)
        => Assert.Equal(result, QaConditionOps.Evaluate(actual, op, expected, 0f));
}
