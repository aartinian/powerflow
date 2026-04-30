using PowerFlow.Core.Models;
using PowerFlow.Core.Parsing;

namespace PowerFlow.Tests.Network;

/// <summary>
/// Unit tests for <see cref="NetworkValidator"/>. Each check method in the validator
/// gets at least one test for the error path and, where applicable, a boundary test to
/// confirm the check is not raised when it should not be.
/// </summary>
public class NetworkValidatorTests
{
    // ── Builder helpers ────────────────────────────────────────────────────────

    private static Bus Slack(int id = 1) =>
        new(id, BusType.Slack, 0, 0, 0, 0, 1.0, 0, 100, 1.1, 0.9);

    private static Bus PQ(int id, double pd = 0) =>
        new(id, BusType.PQ, pd, 0, 0, 0, 1.0, 0, 100, 1.1, 0.9);

    /// <summary>Plain in-service line with negligible impedance.</summary>
    private static Branch Line(int from, int to, bool inService = true) =>
        new(from, to, 0.01, 0.05, 0, 1.0, 0, 0, inService);

    private static Generator Gen(int busId) => new(busId, 100, 0, 200, -200, 1.0, 300, 0, true);

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Case14_IsValid()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        Assert.Empty(result.FatalErrors);
    }

    [Fact]
    public void Validate_Case14_HasNoWarnings()
    {
        var net = MatpowerParser.ParseFile(TestData.Path("case14.m"));
        var result = NetworkValidator.Validate(net);

        Assert.Empty(result.Errors);
    }

    // ── NO_SLACK_BUS ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NoSlackBus_ReturnsError()
    {
        var net = new PowerNetwork(100, [PQ(1), PQ(2)], [Line(1, 2)], []);
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "NO_SLACK_BUS");
    }

    // ── MULTIPLE_SLACK_BUSES ──────────────────────────────────────────────────

    [Fact]
    public void Validate_TwoSlackBuses_ReturnsError()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), Slack(2), PQ(3)],
            [Line(1, 3), Line(2, 3)],
            [Gen(1), Gen(2)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "MULTIPLE_SLACK_BUSES");
    }

    [Fact]
    public void Validate_TwoSlackBuses_ErrorMessageListsBothIds()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), Slack(2), PQ(3)],
            [Line(1, 3), Line(2, 3)],
            [Gen(1), Gen(2)]
        );
        var err = NetworkValidator
            .Validate(net)
            .FatalErrors.Single(e => e.Code == "MULTIPLE_SLACK_BUSES");

        Assert.Contains("1", err.Message);
        Assert.Contains("2", err.Message);
    }

    // ── BRANCH_FROM_BUS_MISSING ───────────────────────────────────────────────

    [Fact]
    public void Validate_BranchFromBusMissing_ReturnsError()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), PQ(2)],
            [Line(99, 2)], // bus 99 not in bus list
            [Gen(1)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "BRANCH_FROM_BUS_MISSING");
    }

    // ── BRANCH_TO_BUS_MISSING ─────────────────────────────────────────────────

    [Fact]
    public void Validate_BranchToBusMissing_ReturnsError()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), PQ(2)],
            [Line(1, 99)], // bus 99 not in bus list
            [Gen(1)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "BRANCH_TO_BUS_MISSING");
    }

    // ── GENERATOR_BUS_MISSING ─────────────────────────────────────────────────

    [Fact]
    public void Validate_GeneratorBusMissing_ReturnsError()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), PQ(2)],
            [Line(1, 2)],
            [Gen(99)] // bus 99 not in bus list
        );
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "GENERATOR_BUS_MISSING");
    }

    // ── NO_IN_SERVICE_BRANCHES ────────────────────────────────────────────────

    [Fact]
    public void Validate_NoInServiceBranches_IsWarningNotError()
    {
        var net = new PowerNetwork(
            100,
            [Slack(1), PQ(2)],
            [Line(1, 2, inService: false)],
            [Gen(1)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid); // warnings don't make result invalid
        Assert.Contains(result.Warnings, w => w.Code == "NO_IN_SERVICE_BRANCHES");
    }

    [Fact]
    public void Validate_SingleBusNetwork_NoInServiceBranchWarningNotRaised()
    {
        // Single-bus networks legitimately have no branches — the check must be skipped.
        var net = new PowerNetwork(100, [Slack(1)], [], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "NO_IN_SERVICE_BRANCHES");
    }

    // ── INVALID_VOLTAGE_LIMITS ────────────────────────────────────────────────

    [Fact]
    public void Validate_VminGreaterThanVmax_IsWarningNotError()
    {
        var badBus = new Bus(2, BusType.PQ, 0, 0, 0, 0, 1.0, 0, 100, vmax: 0.9, vmin: 1.0);
        var net = new PowerNetwork(100, [Slack(1), badBus], [Line(1, 2)], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "INVALID_VOLTAGE_LIMITS");
    }

    [Fact]
    public void Validate_VminEqualToVmax_IsWarning()
    {
        // Vmin == Vmax is the boundary condition that also triggers the warning.
        var badBus = new Bus(2, BusType.PQ, 0, 0, 0, 0, 1.0, 0, 100, vmax: 1.0, vmin: 1.0);
        var net = new PowerNetwork(100, [Slack(1), badBus], [Line(1, 2)], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "INVALID_VOLTAGE_LIMITS");
    }

    [Fact]
    public void Validate_ValidVoltageLimits_NoVoltageLimitWarning()
    {
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [Line(1, 2)], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "INVALID_VOLTAGE_LIMITS");
    }

    // ── PHASE_SHIFT_OUT_OF_RANGE ──────────────────────────────────────────────

    [Fact]
    public void Validate_PhaseShiftExceeds90Deg_IsWarning()
    {
        var xfmr = new Branch(
            1,
            2,
            0.01,
            0.05,
            0,
            1.0,
            phaseShift: 91.0,
            rateA: 0,
            isInService: true
        );
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "PHASE_SHIFT_OUT_OF_RANGE");
    }

    [Fact]
    public void Validate_PhaseShiftNegativeExceeds90Deg_IsWarning()
    {
        var xfmr = new Branch(
            1,
            2,
            0.01,
            0.05,
            0,
            1.0,
            phaseShift: -91.0,
            rateA: 0,
            isInService: true
        );
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "PHASE_SHIFT_OUT_OF_RANGE");
    }

    [Fact]
    public void Validate_PhaseShiftExactly90Deg_NoWarning()
    {
        // |PhaseShift| == 90 is exactly on the boundary; the check is > 90, so no warning.
        var xfmr = new Branch(
            1,
            2,
            0.01,
            0.05,
            0,
            1.0,
            phaseShift: 90.0,
            rateA: 0,
            isInService: true
        );
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "PHASE_SHIFT_OUT_OF_RANGE");
    }

    [Fact]
    public void Validate_PhaseShiftOutOfRange_OutOfServiceBranch_NoWarning()
    {
        // Phase-shift check is only applied to in-service branches.
        var xfmr = new Branch(
            1,
            2,
            0.01,
            0.05,
            0,
            1.0,
            phaseShift: 120.0,
            rateA: 0,
            isInService: false
        );
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "PHASE_SHIFT_OUT_OF_RANGE");
    }

    // ── INVALID_TAP_RATIO ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_NegativeTapRatio_ReturnsError()
    {
        // Negative tap ratio causes division by zero in Y-bus — must be an error.
        var xfmr = new Branch(1, 2, 0.01, 0.05, 0, -0.5, 0, 0, true);
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.Contains(result.FatalErrors, e => e.Code == "INVALID_TAP_RATIO");
    }

    [Fact]
    public void Validate_ZeroTapRatioNormalisedToOne_NoError()
    {
        // Branch constructor normalises 0 → 1.0, so no INVALID_TAP_RATIO should fire.
        var line = new Branch(
            1,
            2,
            0.01,
            0.05,
            0,
            tapRatio: 0,
            phaseShift: 0,
            rateA: 0,
            isInService: true
        );
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [line], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.FatalErrors, e => e.Code == "INVALID_TAP_RATIO");
        Assert.Equal(1.0, line.TapRatio); // confirm normalisation
    }

    [Fact]
    public void Validate_InvalidTapRatio_OutOfServiceBranch_NoError()
    {
        // Tap-ratio check is only applied to in-service branches.
        var xfmr = new Branch(1, 2, 0.01, 0.05, 0, -0.5, 0, 0, isInService: false);
        var net = new PowerNetwork(100, [Slack(1), PQ(2)], [xfmr], [Gen(1)]);
        var result = NetworkValidator.Validate(net);

        Assert.DoesNotContain(result.FatalErrors, e => e.Code == "INVALID_TAP_RATIO");
    }

    // ── ThrowIfInvalid ────────────────────────────────────────────────────────

    [Fact]
    public void ThrowIfInvalid_ThrowsOnFatalErrors()
    {
        var net = new PowerNetwork(100, [PQ(1), PQ(2)], [Line(1, 2)], []);
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        var ex = Assert.Throws<InvalidOperationException>(() => result.ThrowIfInvalid());
        Assert.Contains("NO_SLACK_BUS", ex.Message);
    }

    [Fact]
    public void ThrowIfInvalid_DoesNotThrowOnWarningsOnly()
    {
        // A warnings-only result (out-of-service branch) must not throw.
        var net = new PowerNetwork(
            100,
            [Slack(1), PQ(2)],
            [Line(1, 2, inService: false)],
            [Gen(1)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.True(result.IsValid);
        result.ThrowIfInvalid(); // must not throw
    }

    [Fact]
    public void ThrowIfInvalid_MessageContainsAllErrorCodes()
    {
        // No slack + missing to-bus → 2 separate error codes in the thrown message.
        var net = new PowerNetwork(
            100,
            [PQ(1), PQ(2)],
            [Line(1, 99)], // to-bus 99 missing
            []
        );
        var result = NetworkValidator.Validate(net);
        var ex = Assert.Throws<InvalidOperationException>(() => result.ThrowIfInvalid());

        Assert.Contains("NO_SLACK_BUS", ex.Message);
        Assert.Contains("BRANCH_TO_BUS_MISSING", ex.Message);
    }

    // ── Multiple errors ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleErrors_AllReported()
    {
        // No slack + bad to-bus reference → at least two distinct errors.
        var net = new PowerNetwork(
            100,
            [PQ(1), PQ(2)],
            [Line(1, 99)], // to-bus 99 missing
            [Gen(1)]
        );
        var result = NetworkValidator.Validate(net);

        Assert.False(result.IsValid);
        Assert.True(
            result.FatalErrors.Count() >= 2,
            $"Expected ≥2 errors, got: {string.Join(", ", result.FatalErrors.Select(e => e.Code))}"
        );
    }
}
