using System.Globalization;
using System.Text.RegularExpressions;
using PowerFlow.Core.Models;

namespace PowerFlow.Core.Parsing;

public static class MatpowerParser
{
    public static PowerNetwork ParseFile(string path) =>
        Parse(File.ReadAllText(path));

    public static PowerNetwork Parse(string content)
    {
        var lines = content.Split('\n');

        var baseMva = ParseBaseMva(lines);
        var buses = ExtractBlock(lines, "bus").Select(ParseBus).ToList();
        var generators = ExtractBlock(lines, "gen").Select(ParseGenerator).ToList();
        var branches = ExtractBlock(lines, "branch").Select(ParseBranch).ToList();

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

            yield return s
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
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
            // c[5..7] = rateA/B/C (skipped)
            tapRatio: c[8],
            phaseShift: c[9],
            isInService: (int)c[10] == 1
        );

    private static string StripComment(string line)
    {
        var i = line.IndexOf('%');
        return i >= 0 ? line[..i] : line;
    }
}
