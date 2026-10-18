using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Tests.Workflow.Infrastructure;

public class SpelConditionEvaluatorTests
{
    private readonly SpelConditionEvaluator _evaluator = new();

    private readonly Dictionary<string, object?> _vars = new()
    {
        ["docType"] = "CONTRACT",
        ["amount"] = 300000000m,
        ["customerTier"] = "B",
        ["department"] = "KD",
    };

    [Fact]
    public void BlankConditionAlwaysApplies()
    {
        Assert.True(_evaluator.Evaluate(null, _vars));
        Assert.True(_evaluator.Evaluate("  ", _vars));
    }

    [Fact]
    public void ComparesAmountAgainstThreshold()
    {
        Assert.False(_evaluator.Evaluate("amount > 500000000", _vars));
        Assert.True(_evaluator.Evaluate("amount > 500000000", new Dictionary<string, object?> { ["amount"] = 1200000000m }));
        Assert.True(_evaluator.Evaluate("docType == 'CONTRACT' or amount > 500000000", _vars));
        Assert.False(_evaluator.Evaluate("customerTier == 'A' and department == 'KD'", _vars));
    }

    [Theory]
    [InlineData("secret > 1")]
    [InlineData("T(java.lang.Runtime).getRuntime().exec('id') == null")]
    [InlineData("'a'.toUpperCase() == 'A'")]
    [InlineData("amount = 1")]
    [InlineData("@workflowEngine != null")]
    [InlineData("amount +")]
    [InlineData("amount")]
    [InlineData("'true'")]
    [InlineData("currency == 'VND'")]
    public void RejectsUnsafeOrNonBooleanExpressions(string expression)
    {
        var ex = Assert.Throws<DomainException>(() => _evaluator.Evaluate(expression, _vars));
        Assert.Equal(WorkflowError.WF_CONDITION_ERROR, ex.Code);
    }

    [Fact]
    public void IgnoresVariablesOutsideWhitelistEvenIfProvided()
    {
        var withExtra = new Dictionary<string, object?>
        {
            ["amount"] = 1m,
            ["_summary"] = new Dictionary<string, object?> { ["title"] = "x" },
        };
        Assert.Throws<DomainException>(() => _evaluator.Evaluate("_summary != null", withExtra));
    }

    [Fact]
    public void RejectsOverlongExpressions()
    {
        var expression = string.Concat(Enumerable.Repeat("amount > 1 or ", 40)) + "amount > 1";
        Assert.Throws<DomainException>(() => _evaluator.Evaluate(expression, _vars));
    }

    [Theory]
    [InlineData("addendumType != 'EXTENSION' or amount > 500000000", true)]
    [InlineData("scope == 'GLOBAL' or maxDeltaPct > 10", false)]
    [InlineData("not (amount < 100) && isAdjustment == false", true)]
    [InlineData("amount ge 300000000 AND customerTier eq 'B'", true)]
    [InlineData("(amount / 1000000) % 7 == 6", true)]
    [InlineData("customerTier matches '[A-C]'", true)]
    [InlineData("isAdjustment ? false : true", true)]
    [InlineData("-amount < 0", true)]
    [InlineData("'it''s' == \"it's\"", true)]
    public void SupportsTheSpelSubsetUsedBySeedDefinitions(string expression, bool expected)
    {
        var vars = new Dictionary<string, object?>(_vars)
        {
            ["addendumType"] = "PRICE_CHANGE",
            ["scope"] = "CUSTOMER",
            ["maxDeltaPct"] = 5m,
            ["isAdjustment"] = false,
        };
        Assert.Equal(expected, _evaluator.Evaluate(expression, vars));
    }

    [Theory]
    [InlineData("amount > 'x'")]
    [InlineData("amount and true")]
    [InlineData("1 < 2 < 3")]
    [InlineData("amount / 0 > 1")]
    public void TypeErrorsAreConditionErrors(string expression)
    {
        var ex = Assert.Throws<DomainException>(() => _evaluator.Evaluate(expression, _vars));
        Assert.Equal(WorkflowError.WF_CONDITION_ERROR, ex.Code);
        Assert.NotNull(ex.Args["cause"]);
    }
}
