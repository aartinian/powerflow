using System.Numerics;

namespace PowerFlow.Core.Network;

/// <summary>
/// Sparse admittance matrix: diagonal entries plus per-row off-diagonal adjacency lists.
/// Reduces per-iteration work in injection and Jacobian loops from O(n²) to O(n + nnz).
/// </summary>
public sealed class SparseYbus
{
    /// <summary>Number of buses (order of the admittance matrix).</summary>
    public int N { get; }

    /// <summary>Real part of the diagonal admittance Y[i,i]: shunt conductance + Σ branch conductances.</summary>
    public double[] Gd { get; }

    /// <summary>Imaginary part of the diagonal admittance Y[i,i]: shunt susceptance + Σ branch susceptances.</summary>
    public double[] Bd { get; }

    /// <summary>
    /// Off-diagonal non-zeros. <c>OffDiag[i]</c> lists every j ≠ i for which Y[i,j] ≠ 0,
    /// as (j, G_ij, B_ij) tuples. Enables O(nnz) injection and Jacobian loops.
    /// </summary>
    public (int J, double G, double B)[][] OffDiag { get; }

    /// <summary>Initializes a new sparse admittance matrix with the specified diagonal and off-diagonal structure.</summary>
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
