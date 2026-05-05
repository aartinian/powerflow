namespace PowerFlow.Core.Solver;

/// <summary>
/// Solved operating point of one in-service generator.
/// Pg includes the distributed-slack correction (α·λ·BaseMVA) allocated
/// proportionally to Pmax; without distributed slack it equals the input dispatch.
/// Qg is the bus-level reactive generation divided equally among all in-service
/// generators at that bus — a documented approximation, since the solver tracks
/// only bus totals, not per-generator reactive allocation.
/// </summary>
public class GeneratorResult
{
    public int    BusId    { get; }
    public double Pg       { get; } // MW   — dispatched real power (+ distributed-slack correction)
    public double Qg       { get; } // MVAr — bus reactive output shared equally among bus generators
    public bool   IsAtQmax { get; } // true when the bus was switched PV→PQ at its reactive ceiling
    public bool   IsAtQmin { get; } // true when the bus was switched PV→PQ at its reactive floor

    public GeneratorResult(int busId, double pg, double qg, bool isAtQmax, bool isAtQmin)
    {
        BusId    = busId;
        Pg       = pg;
        Qg       = qg;
        IsAtQmax = isAtQmax;
        IsAtQmin = isAtQmin;
    }
}
