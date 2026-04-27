using System.Numerics;

namespace PowerFlow.Core.Network;

/// <summary>
/// Sparse admittance matrix: diagonal entries plus per-row off-diagonal adjacency lists.
/// Reduces per-iteration work in injection and Jacobian loops from O(n²) to O(n + nnz).
/// </summary>
public sealed class SparseYbus
{
    public int N { get; }

    /// <summary>Real and imaginary parts of diagonal Y[i,i].</summary>
    public double[] Gd { get; }
    public double[] Bd { get; }

    /// <summary>Off-diagonal non-zeros: OffDiag[i] lists every j≠i where Y[i,j]≠0.</summary>
    public (int J, double G, double B)[][] OffDiag { get; }

    public SparseYbus(int n, double[] gd, double[] bd, (int J, double G, double B)[][] offDiag)
    {
        N = n;
        Gd = gd;
        Bd = bd;
        OffDiag = offDiag;
    }

    /// <summary>Element access for tests and diagnostics; O(degree) for off-diagonal entries.</summary>
    public Complex this[int i, int j]
    {
        get
        {
            if (i == j)
                return new Complex(Gd[i], Bd[i]);
            foreach (var (k, g, b) in OffDiag[i])
                if (k == j)
                    return new Complex(g, b);
            return Complex.Zero;
        }
    }
}
