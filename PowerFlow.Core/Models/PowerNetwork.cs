namespace PowerFlow.Core.Models;

/// <summary>
/// A complete network snapshot: base MVA plus the buses, branches, and
/// generators that define the system. Buses are sorted by ID on construction
/// to give the solver a stable, parse-order-independent index mapping.
/// Use <see cref="IndexOf"/> to map a bus ID to its 0-based array index;
/// validate first with <see cref="NetworkValidator"/>.
/// </summary>
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

        // Reject duplicate bus IDs early — duplicates produce silent NR failures
        // that are extremely hard to diagnose from solver output alone.
        for (int i = 1; i < Buses.Count; i++)
        {
            if (Buses[i].Id == Buses[i - 1].Id)
                throw new ArgumentException(
                    $"Duplicate bus ID {Buses[i].Id} in network. "
                        + "Each bus must have a unique integer identifier."
                );
        }

        _busIndex = Buses.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
    }

    public int IndexOf(int busId) =>
        _busIndex.TryGetValue(busId, out var idx)
            ? idx
            : throw new InvalidOperationException(
                $"Bus {busId} is not in the network. "
                    + "Validate the network with NetworkValidator before solving."
            );
}
