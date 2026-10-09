//
//  RopeElasticityTests.cs
//  Hangly.Core.Tests
//

using Hangly.Core.Geometry;
using Hangly.Core.Models;
using Hangly.Core.Physics;
using Xunit;

namespace Hangly.Core.Tests;

/// <summary>
/// The opt-in stretchy-rope behaviour: normal mode must stay exactly as rigid as it has
/// always been, and stretchy mode must both give visibly under a hard pull and return to
/// the same near-rigid rest state once released.
/// </summary>
public class RopeElasticityTests
{
    private static readonly Vec2 Anchor = new(260, 15);
    private const double Frame120 = 1.0 / 120.0;

    private static RopeSimulation MakeRope()
    {
        var rope = new RopeSimulation(RopeConfiguration.Default, Anchor);
        rope.Start();
        return rope;
    }

    private static void Run(RopeSimulation rope, double seconds)
    {
        for (int tick = 0; tick < (int)(seconds / Frame120); tick++)
        {
            rope.Step(Frame120);
        }
    }

    [Fact(DisplayName = "Normal elasticity behaves exactly like the rope always has")]
    public void NormalModeMatchesTheShippedCeiling()
    {
        RopeSimulation rope = MakeRope();
        rope.SetElasticity(RopeElasticity.Normal);
        Run(rope, 1);
        rope.BeginDrag(rope.Points[20].Position);

        Vec2 position = rope.Points[20].Position;
        double worstStretch = 0;

        for (int tick = 0; tick < 1200; tick++)
        {
            double direction = (tick / 18) % 2 == 0 ? 1 : -1;
            position += new Vec2(direction * 3000 * Frame120, Math.Sin(tick * 0.05) * 12);
            rope.UpdateDrag(position, new Vec2(direction * 3000, 0));
            rope.Step(Frame120);
            worstStretch = Math.Max(worstStretch, rope.MeasuredMaximumStretch);
        }

        Assert.True(
            worstStretch <= rope.Configuration.MaxStretchRatio + 1e-9,
            $"worst stretch was {worstStretch}");
    }

    [Fact(DisplayName = "Stretchy mode visibly stretches under a hard, held pull")]
    public void StretchyModeStretchesPastTheNormalCeiling()
    {
        RopeSimulation rope = MakeRope();
        rope.SetElasticity(RopeElasticity.Stretchy);
        Run(rope, 1);

        double heldDistance = 20 * rope.Configuration.SegmentLength;
        Vec2 farTarget = Anchor + new Vec2(0, heldDistance * 1.2);

        rope.BeginDrag(rope.Points[20].Position);
        double worstStretch = 0;
        for (int tick = 0; tick < 240; tick++)
        {
            rope.UpdateDrag(farTarget, Vec2.Zero);
            rope.Step(Frame120);
            worstStretch = Math.Max(worstStretch, rope.MeasuredMaximumStretch);
        }

        // Pulled a fifth further than the rope's own length — well past what the normal
        // 2%-ish ceiling would ever allow, and past MaxStretchRatio at rest.
        Assert.True(
            worstStretch > RopeConfiguration.Default.MaxStretchRatio + 0.1,
            $"worst stretch was {worstStretch}");
    }

    [Fact(DisplayName = "Stretchy mode springs back once the charm is let go")]
    public void StretchyModeRecoversAfterRelease()
    {
        RopeSimulation rope = MakeRope();
        rope.SetElasticity(RopeElasticity.Stretchy);
        Run(rope, 1);

        double heldDistance = 20 * rope.Configuration.SegmentLength;
        Vec2 farTarget = Anchor + new Vec2(0, heldDistance * 1.2);

        rope.BeginDrag(rope.Points[20].Position);
        for (int tick = 0; tick < 240; tick++)
        {
            rope.UpdateDrag(farTarget, Vec2.Zero);
            rope.Step(Frame120);
        }

        Assert.True(rope.MeasuredMaximumStretch > RopeConfiguration.Default.MaxStretchRatio + 0.1);

        rope.EndDrag();
        Run(rope, 2);

        // Back to reading as an ordinary, near-rigid rope — not merely under the raised
        // stretchy ceiling, but back at the same rest state normal mode holds.
        Assert.True(
            rope.MeasuredMaximumStretch <= RopeConfiguration.Default.MaxStretchRatio + 0.05,
            $"worst stretch was {rope.MeasuredMaximumStretch}");
    }
}
