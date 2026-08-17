namespace Advisory.Api.Nexus;

/// <summary>
/// Semver-tolerant version comparison (shared by the safe-version recommender). Compares dotted numeric
/// components left-to-right, falling back to ordinal when a component doesn't parse — good enough to
/// order a registry's version list "nearest above" / "latest" without a full semver dependency. Mirrors
/// the comparison OsvSource uses to pick the lowest fixed version.
/// </summary>
public static class VersionOrder
{
    public static int Compare(string a, string b)
    {
        static int[] Parts(string v) => v.Split('.', '-', '+')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToArray();
        var pa = Parts(a); var pb = Parts(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return string.CompareOrdinal(a, b);
    }

    public static readonly IComparer<string> Comparer = System.Collections.Generic.Comparer<string>.Create(Compare);
}
