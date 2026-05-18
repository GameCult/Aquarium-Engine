using System.Globalization;
using System.Text;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Debug;

public static class FractalDebugDump
{
    public static string Build(
        FractalOwnershipTree tree,
        IReadOnlyList<AquariumFractalSummary> summaries,
        IReadOnlyList<AquariumSelectedCut> selectedCut)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(selectedCut);

        var builder = new StringBuilder();
        foreach (var domain in tree.Domains)
        {
            builder.AppendLine($"domain {domain.Key} kind={domain.Kind} parent={domain.ParentKey}");
        }

        builder.AppendLine($"nodes {tree.Nodes.Count} claims {tree.Claims.Count} summaries {summaries.Count} cut {selectedCut.Count}");
        foreach (var node in tree.Nodes)
        {
            builder.AppendLine($"node {node.Key} op={node.Operation} claims={node.ClaimCount} bounds={FormatBounds(node.BoundsMinMax)}");
        }

        foreach (var claim in tree.Claims)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"claim {claim.Key} payload={claim.PayloadKind} center=({claim.Center.X:0.###},{claim.Center.Y:0.###}) radii=({claim.Radii.X:0.###},{claim.Radii.Y:0.###}) amp={claim.Amplitude:0.###} tags={claim.Tags}"));
        }

        foreach (var summary in summaries)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"summary {summary.NodeKey} heightError={summary.MaxHeightError:0.###} cost={summary.EstimatedCost:0.###} descendants={summary.DescendantCount}"));
        }

        foreach (var cut in selectedCut)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"cut {cut.NodeKey} score={cut.Score:0.###} fade={cut.Fade:0.###} summary={cut.UsesSummary} requestChildren={cut.RequestedChildren}"));
        }

        return builder.ToString();
    }

    private static string FormatBounds(System.Numerics.Vector4 bounds)
    {
        return string.Create(CultureInfo.InvariantCulture, $"({bounds.X:0.###},{bounds.Y:0.###})-({bounds.Z:0.###},{bounds.W:0.###})");
    }
}
