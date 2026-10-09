//
//  EnvelopeTests.cs
//  Hangly.Core.Tests
//

using Hangly.Core.Geometry;
using Hangly.Core.Models;
using Hangly.Core.Physics;
using Xunit;

namespace Hangly.Core.Tests;

/// <summary>Checks that the rope stays inside the canvas it was fitted to.</summary>
/// <remarks>
/// The overlay is a window, so anything drawn outside its canvas is not drawn at all —
/// the charm is cut off by a straight vertical edge in mid-swing. That was shipped, and
/// these are the measurements that say it is fixed.
///
/// <para>The canvas is restated here rather than read from <c>OverlayMetrics</c> because
/// that type lives in Hangly.App, which references WinUI and Win2D and cannot be
/// referenced back. The two must agree; if they drift, this suite goes green while the
/// overlay clips, so <c>OverlayMetrics.CanvasSize</c> carries a pointer to here.</para>
/// </remarks>
public class EnvelopeTests
{
    private const double BaseWidth = 220;
    private const double BaseHeight = 360;
    private const double Frame120 = 1.0 / 120.0;

    /// <summary>The overlay's canvas, as <c>OverlayMetrics.CanvasSize</c> computes it.</summary>
    private static Size Canvas(double charmSize, double ropeLength, RopeElasticity elasticity = RopeElasticity.Normal)
    {
        Size room = RopeConfiguration.Layout.CanvasScale(charmSize, ropeLength);
        double rope = BaseHeight * RopeConfiguration.Layout.LengthFraction * ropeLength;
        double swing = rope * RopeElasticityTable.PhysicsOf(elasticity).MaximumReachRatio;
        double reach = BaseHeight * RopeConfiguration.Layout.TailFraction * charmSize
            / RopeConfiguration.Layout.CharmHaloExtent;

        double height = Math.Max(
            BaseHeight * room.Height,
            (BaseHeight * RopeConfiguration.Layout.AnchorFraction) + swing + reach);

        return new Size(Math.Max(BaseWidth * room.Width, 2 * (swing + reach)), height);
    }

    /// <summary>
    /// The height <c>RopeConfiguration.Fitted</c> must be given, as
    /// <c>OverlayMetrics.ReferenceHeight</c> computes it — restated for the same reason
    /// <see cref="Canvas"/> is.
    /// </summary>
    private static double ReferenceHeight(double charmSize, double ropeLength) =>
        BaseHeight * RopeConfiguration.Layout.CanvasScale(charmSize, ropeLength).Height;

    /// <summary>A rope hung the way the overlay hangs one.</summary>
    private static RopeSimulation Hang(
        int charmCount,
        double charmSize,
        double ropeLength,
        RopeStyle style,
        RopeElasticity elasticity = RopeElasticity.Normal)
    {
        Size canvas = Canvas(charmSize, ropeLength, elasticity);
        var rope = new RopeSimulation(style: style, timeProfile: RopeTimeProfileTable.Baseline);
        rope.SetElasticity(elasticity);

        var metrics = new List<CharmMetrics>();
        var beads = new List<IReadOnlyList<CharmBead>>();
        for (int index = 0; index < charmCount; index++)
        {
            CharmCatalogEntry entry = CharmCatalog.All[index];
            metrics.Add(new CharmMetrics(entry.Mass, entry.RadiusRatio, 0.90));
            beads.Add([]);
        }

        rope.SetCharmStack(metrics);
        rope.SetBeads(beads);

        // Fitted against the reference height, not the (possibly stretch-widened) canvas
        // height, exactly as OverlayWindow.Reposition now does — see ReferenceHeight.
        rope.Fit(new Size(canvas.Width, ReferenceHeight(charmSize, ropeLength)), charmSize, ropeLength);
        rope.Start();
        return rope;
    }

    /// <summary>The widest the charms reach, over <paramref name="seconds"/> of swing.</summary>
    private static (double Left, double Right) Sweep(RopeSimulation rope, double seconds)
    {
        double left = double.MaxValue;
        double right = double.MinValue;

        for (int tick = 0; tick < seconds * 120; tick++)
        {
            rope.Step(Frame120);
            foreach (CharmPlacement charm in rope.Snapshot().Charms)
            {
                left = Math.Min(left, charm.Center.X - charm.Radius);
                right = Math.Max(right, charm.Center.X + charm.Radius);
            }
        }

        return (left, right);
    }

    public static TheoryData<RopeStyle, int, double, double> Combinations()
    {
        var data = new TheoryData<RopeStyle, int, double, double>();
        foreach (RopeStyle style in Enum.GetValues<RopeStyle>())
        {
            for (int count = 1; count <= CharmStack.MaximumCount; count++)
            {
                // The ends of both sliders and the shipped middle, because the width is
                // a different trade at each: OverlaySettings clamps both to 0.5..2.0.
                foreach (double charmSize in new[] { 0.5, 1.0, 2.0 })
                {
                    foreach (double ropeLength in new[] { 0.5, 1.0, 2.0 })
                    {
                        data.Add(style, count, charmSize, ropeLength);
                    }
                }
            }
        }

        return data;
    }

    [Theory(DisplayName = "No charm is ever drawn outside the canvas")]
    [MemberData(nameof(Combinations))]
    public void SwingStaysInsideTheCanvas(RopeStyle style, int charmCount, double charmSize, double ropeLength)
    {
        Size canvas = Canvas(charmSize, ropeLength);
        RopeSimulation rope = Hang(charmCount, charmSize, ropeLength, style);

        // Twelve seconds is long enough to cover the release and the whole settle; the
        // first swing is the widest, so a shorter run would pass on styles that ring.
        (double left, double right) = Sweep(rope, 12);

        Assert.True(left >= 0, $"clipped {-left:F1}pt off the left of a {canvas.Width:F0}pt canvas");
        Assert.True(
            right <= canvas.Width,
            $"clipped {right - canvas.Width:F1}pt off the right of a {canvas.Width:F0}pt canvas");
    }

    [Theory(DisplayName = "No charm is ever dragged outside the canvas")]
    [MemberData(nameof(Combinations))]
    public void DragStaysInsideTheCanvas(RopeStyle style, int charmCount, double charmSize, double ropeLength)
    {
        Size canvas = Canvas(charmSize, ropeLength);
        RopeSimulation rope = Hang(charmCount, charmSize, ropeLength, style);
        rope.Step(Frame120);

        // The drag clamps the held node to MaximumReachRatio of the cord above it, so the
        // charm can be taken anywhere on that circle — far outside the arc a released rope
        // swings through. Walking the whole circle is what makes this the real envelope
        // rather than the one the launch animation happens to use.
        CharmPlacement lowest = rope.Snapshot().Charms[^1];
        Assert.True(rope.BeginDrag(lowest.Center), "the lowest charm could not be grabbed");

        double left = double.MaxValue;
        double right = double.MinValue;

        for (int degree = 0; degree <= 360; degree += 2)
        {
            double radians = degree * Math.PI / 180;

            // Pulled well past the reach limit on purpose: the clamp is the thing under
            // test, so the target has to be outside it for the clamp to be what stops it.
            var target = new Vec2(
                rope.Anchor.X + (Math.Cos(radians) * canvas.Width * 2),
                rope.Anchor.Y + (Math.Sin(radians) * canvas.Width * 2));

            rope.UpdateDrag(target, Vec2.Zero);
            for (int tick = 0; tick < 4; tick++)
            {
                rope.Step(Frame120);
            }

            foreach (CharmPlacement charm in rope.Snapshot().Charms)
            {
                left = Math.Min(left, charm.Center.X - charm.Radius);
                right = Math.Max(right, charm.Center.X + charm.Radius);
            }
        }

        rope.EndDrag();

        Assert.True(left >= 0, $"dragged {-left:F1}pt off the left of a {canvas.Width:F0}pt canvas");
        Assert.True(
            right <= canvas.Width,
            $"dragged {right - canvas.Width:F1}pt off the right of a {canvas.Width:F0}pt canvas");
    }

    /// <summary>
    /// The same drag circle as <see cref="DragStaysInsideTheCanvas"/>, for a stretchy
    /// rope's much larger reach — this is the regression a hard-coded
    /// <c>RopeConfiguration.Default.MaximumReachRatio</c> in <c>OverlayMetrics.CanvasSize</c>
    /// would reintroduce, and what was actually seen: the cord tracked a far drag
    /// correctly, but the charm ran off the bottom of a window still sized for the
    /// near-rigid rope.
    /// </summary>
    /// <remarks>
    /// The anchor sits a hair below the canvas's own top edge, so — unlike the width,
    /// which is free either side of it — there is no meaningful "up" budget to test:
    /// a real cursor cannot reach above the display's own top edge either. Only the
    /// downward direction is a genuine, reachable regression, so only the bottom is
    /// asserted here, matching what <see cref="DragStaysInsideTheCanvas"/> already
    /// chooses to assert (left and right, never top).
    /// </remarks>
    /// <summary>
    /// A small, deliberately non-exhaustive sample of <see cref="Combinations"/> — enough
    /// to cross every dimension at least once (a light style and a heavy one, one charm
    /// and a full stack, both slider extremes) without paying for the full 243-case grid
    /// <see cref="Combinations"/> exists for. The geometry this test is checking does not
    /// vary per style or charm count, only the fit formula's own inputs (charm size, rope
    /// length) do, so this is not a coverage compromise the way it would be for the solver
    /// tests the full grid was written for.
    /// </summary>
    public static TheoryData<RopeStyle, int, double, double> StretchySample()
    {
        var data = new TheoryData<RopeStyle, int, double, double>
        {
            { RopeStyle.Thread, 1, 1.0, 1.0 },
            { RopeStyle.Thread, 1, 0.5, 0.5 },
            { RopeStyle.Thread, 1, 2.0, 2.0 },
            { RopeStyle.Leather, 3, 0.5, 2.0 },
            { RopeStyle.Leather, 3, 2.0, 0.5 },
        };
        return data;
    }

    [Theory(DisplayName = "A stretchy rope's charm is never dragged below the canvas")]
    [MemberData(nameof(StretchySample))]
    public void StretchyDragStaysInsideTheCanvas(RopeStyle style, int charmCount, double charmSize, double ropeLength)
    {
        Size canvas = Canvas(charmSize, ropeLength, RopeElasticity.Stretchy);
        RopeSimulation rope = Hang(charmCount, charmSize, ropeLength, style, RopeElasticity.Stretchy);
        rope.Step(Frame120);

        CharmPlacement lowest = rope.Snapshot().Charms[^1];
        Assert.True(rope.BeginDrag(lowest.Center), "the lowest charm could not be grabbed");

        double left = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;

        for (int degree = 0; degree <= 360; degree += 2)
        {
            double radians = degree * Math.PI / 180;
            var target = new Vec2(
                rope.Anchor.X + (Math.Cos(radians) * canvas.Width * 2),
                rope.Anchor.Y + (Math.Sin(radians) * canvas.Width * 2));

            rope.UpdateDrag(target, Vec2.Zero);
            for (int tick = 0; tick < 4; tick++)
            {
                rope.Step(Frame120);
            }

            foreach (CharmPlacement charm in rope.Snapshot().Charms)
            {
                left = Math.Min(left, charm.Center.X - charm.Radius);
                right = Math.Max(right, charm.Center.X + charm.Radius);
                bottom = Math.Max(bottom, charm.Center.Y + charm.Radius);
            }
        }

        rope.EndDrag();

        Assert.True(left >= 0, $"dragged {-left:F1}pt off the left of a {canvas.Width:F0}pt canvas");
        Assert.True(
            right <= canvas.Width,
            $"dragged {right - canvas.Width:F1}pt off the right of a {canvas.Width:F0}pt canvas");
        Assert.True(
            bottom <= canvas.Height,
            $"dragged {bottom - canvas.Height:F1}pt off the bottom of a {canvas.Height:F0}pt canvas");
    }

    [Fact(DisplayName = "Re-fitting a rope to a wider canvas moves it rather than flinging it")]
    public void ResizeCarriesTheRopeWithTheAnchor()
    {
        RopeSimulation rope = Hang(1, 1, 1, RopeStyle.Leather);
        Sweep(rope, 12);

        Vec2 before = rope.Snapshot().Charms[0].Center;
        Size wider = Canvas(1.5, 1);
        double shift = RopeConfiguration.Layout.AnchorIn(wider).X - rope.Anchor.X;

        rope.SetCharmSize(1.5, wider);
        Vec2 after = rope.Snapshot().Charms[0].Center;

        // The charm sits where it sat, plus however far the anchor moved. Nothing has
        // been stepped yet, so this is the translation alone: any difference here is the
        // rope being displaced relative to its own anchor, which is what the solver turns
        // into a fling on the next step.
        Assert.Equal(before.X + shift, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }
}
