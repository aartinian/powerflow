namespace PowerFlow.Core.Models;

/// <summary>
/// A complete network snapshot: base MVA plus the buses, branches, and
/// generators that define the system. Buses are sorted by ID on construction
/// to give the solver a stable, parse-order-independent index mapping.
/// Use <see cref="IndexOf"/> to map a bus ID to its 0-based array index;
/// validate first with <see cref="Validation.NetworkValidator"/>.
/// </summary>
public class PowerNetwork
{
    /// <summary>System base power in MVA, used to convert between per-unit and engineering units.</summary>
    public double BaseMva { get; }

    /// <summary>All buses in the network, sorted by ID for a stable index mapping.</summary>
    public IReadOnlyList<Bus> Buses { get; }

    /// <summary>All transmission lines and transformers in the network.</summary>
    public IReadOnlyList<Branch> Branches { get; }

    /// <summary>All generators in the network.</summary>
    public IReadOnlyList<Generator> Generators { get; }

    // Bus ID → 0-based array index used by the solver for Jacobian and state arrays.
    // Buses are sorted by ID on construction so this mapping is stable regardless of parse order.
    private readonly Dictionary<int, int> _busIndex;

    /// <summary>
    /// Initializes a new network. Buses are sorted by ID to provide stable index mapping
    /// for the solver; duplicate IDs raise <see cref="ArgumentException"/>.
    /// </summary>
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

    /// <summary>
    /// Maps a bus ID to its 0-based array index used by the solver for Jacobian and state arrays.
    /// Throws <see cref="InvalidOperationException"/> if the bus is not in the network.
    /// </summary>
    public int IndexOf(int busId) =>
        _busIndex.TryGetValue(busId, out var idx)
            ? idx
            : throw new InvalidOperationException(
                $"Bus {busId} is not in the network. "
                    + "Validate the network with Validation.NetworkValidator before solving."
            );
}
