namespace PowerFlow.Core.Models;

public class PowerNetwork
{
    public double BaseMva { get; }
    public IReadOnlyList<Bus> Buses { get; }
    public IReadOnlyList<Branch> Branches { get; }
    public IReadOnlyList<Generator> Generators { get; }

    // Bus ID → 0-based array index used by the solver for Jacobian and state arrays.
    // Buses are sorted by ID on construction so this mapping is stable regardless of parse order.
    private readonly Dictionary<int, int> _busIndex;

    public PowerNetwork(
        double baseMva,
        IEnumerable<Bus> buses,
        IEnumerable<Branch> branches,
        IEnumerable<Generator> generators
    )
    {
        BaseMva = baseMva;
        Buses = buses.OrderBy(b => b.Id).ToList();
        Branches = branches.ToList();
        Generators = generators.ToList();

        _busIndex = Buses.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
    }

    public int IndexOf(int busId) => _busIndex[busId];
}
