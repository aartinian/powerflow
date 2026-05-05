using System.Globalization;
using System.Text.RegularExpressions;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Parsing;

/// <summary>
/// Parses MATPOWER <c>case*.m</c> files into a <see cref="PowerNetwork"/>.
/// Reads <c>mpc.baseMVA</c>, <c>mpc.bus</c>, <c>mpc.gen</c>, and <c>mpc.branch</c>
/// blocks; ignores <c>mpc.gencost</c> and any custom fields. Assumes the
/// standard MATPOWER column layout — non-standard cases may need a custom parser.
/// </summary>
public static class MatpowerParser
{
    public static PowerNetwork ParseFile(string path) => Parse(File.ReadAllText(path));

    public static PowerNetwork Parse(string content)
    {
        var lines = content.Split('\n');

        var baseMva = ParseBaseMva(lines);
        var buses = ExtractBlock(lines, "bus").Select(ParseBus).ToList();
        var generators = ExtractBlock(lines, "gen").Select(ParseGenerator).ToList();
        var branches = ExtractBlock(lines, "branch").Select(ParseBranch).ToList();

        FoldShunts(lines, buses);

        return new PowerNetwork(baseMva, buses, branches, generators);
    }

    private static double ParseBaseMva(string[] lines)
    {
        foreach (var line in lines)
        {
            var s = StripComment(line).Trim();
            if (!s.StartsWith("mpc.baseMVA"))
                continue;

            var rhs = s.Split('=')[1].Trim().TrimEnd(';').Trim();
            return double.Parse(rhs, CultureInfo.InvariantCulture);
        }
        throw new FormatException("mpc.baseMVA not found in case file.");
    }

    // Yields one double[] per data row for the named MATPOWER block.
    // Assumes '[' appears on the same line as the assignment — standard MATPOWER format.
    private static IEnumerable<double[]> ExtractBlock(string[] lines, string name)
    {
        bool inside = false;

        foreach (var line in lines)
        {
            var s = StripComment(line).Trim();

            if (!inside)
            {
                // \b prevents "mpc.gen" from matching "mpc.gencost"
                if (Regex.IsMatch(s, $@"mpc\.{name}\b") && s.Contains('['))
                    inside = true;
                continue;
            }

            if (s.StartsWith(']'))
                yield break;

            s = s.TrimEnd(';').Trim();
            if (string.IsNullOrWhiteSpace(s))
                continue;

            yield return s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();
        }
    }

    // MATPOWER bus columns: bus_i type Pd Qd Gs Bs area Vm Va baseKV zone Vmax Vmin
    private static Bus ParseBus(double[] c) =>
        new(
            id: (int)c[0],
            type: (BusType)(int)c[1],
            pd: c[2],
            qd: c[3],
            gs: c[4],
            bs: c[5],
            // c[6] = area  (skipped)
            vm: c[7],
            va: c[8],
            baseKv: c[9],
            // c[10] = zone (skipped)
            vmax: c[11],
            vmin: c[12]
        );

    // MATPOWER gen columns: bus Pg Qg Qmax Qmin Vg mBase status Pmax Pmin
    private static Generator ParseGenerator(double[] c) =>
        new(
            busId: (int)c[0],
            pg: c[1],
            qg: c[2],
            qmax: c[3],
            qmin: c[4],
            vg: c[5],
            // c[6] = mBase (skipped)
            isInService: (int)c[7] == 1,
            pmax: c[8],
            pmin: c[9]
        );

    // MATPOWER branch columns: fbus tbus r x b rateA rateB rateC ratio angle status angmin angmax
    private static Branch ParseBranch(double[] c) =>
        new(
            fromBus: (int)c[0],
            toBus: (int)c[1],
            r: c[2],
            x: c[3],
            b: c[4],
            rateA: c[5],
            rateB: c[6],
            rateC: c[7],
            tapRatio: c[8],
            phaseShift: c[9],
            isInService: (int)c[10] == 1
        );

    // mpc.shunt columns: bus Gs Bs (MW / MVAr at 1 pu — same units as mpc.bus cols 5-6).
    // Shunt admittances are additive with the inline Gs/Bs already in the bus data.
    // Multiple rows for the same bus are accumulated (non-standard but safe to handle).
    private static void FoldShunts(string[] lines, List<Bus> buses)
    {
        var shunts = new Dictionary<int, (double Gs, double Bs)>();
        foreach (var c in ExtractBlock(lines, "shunt"))
        {
            int id = (int)c[0];
            shunts[id] = shunts.TryGetValue(id, out var prev)
                ? (prev.Gs + c[1], prev.Bs + c[2])
                : (c[1], c[2]);
        }

        if (shunts.Count == 0)
            return;

        for (int i = 0; i < buses.Count; i++)
        {
            var b = buses[i];
            if (!shunts.TryGetValue(b.Id, out var s))
                continue;
            buses[i] = new Bus(
                b.Id,
                b.Type,
                b.Pd,
                b.Qd,
                b.Gs + s.Gs,
                b.Bs + s.Bs,
                b.Vm,
                b.Va,
                b.BaseKv,
                b.Vmax,
                b.Vmin
            );
        }
    }

    private static string StripComment(string line)
    {
        var i = line.IndexOf('%');
        return i >= 0 ? line[..i] : line;
    }
}
