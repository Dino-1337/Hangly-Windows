//
//  RopeElasticity.cs
//  Hangly
//
//  How far the cord may give under a pull, for people who asked it to give more.
//

namespace Hangly.Core.Models;

/// <summary>What the person chose in Customize → Behaviour → Stretchy rope.</summary>
public enum RopeElasticity
{
    Normal,
    Stretchy,
}

/// <summary>What stretchy mode does to the solver.</summary>
/// <param name="MaxStretchRatioBonus">
/// Added to whatever the style already set for <see cref="Physics.RopeConfiguration.MaxStretchRatio"/>,
/// so a stretchy leather rope is still stiffer than a stretchy thread — elasticity scales
/// what the material decided rather than replacing it.
/// </param>
/// <param name="MaximumReachRatio">
/// Replaces <see cref="Physics.RopeConfiguration.MaximumReachRatio"/> outright. Unlike the
/// stretch ratio this was never per-style to begin with, so there is nothing to add to.
/// </param>
/// <remarks>
/// <b>What it leaves alone, on purpose:</b> gravity, length, segment count and damping.
/// Those decide what the rope looks like and how it settles, and stretchy mode is a
/// promise that it looks and swings exactly the same — it only gives more before the
/// solver pulls it back.
/// </remarks>
public readonly record struct RopeElasticityPhysics(
    double MaxStretchRatioBonus,
    double MaximumReachRatio);

/// <summary>The two elasticity profiles.</summary>
public static class RopeElasticityTable
{
    public static RopeElasticityPhysics PhysicsOf(RopeElasticity elasticity) => elasticity switch
    {
        // Generous, not unbounded: a real cursor is bounded by the monitor, and the
        // overlay window has to be at least this large on every side to draw a drag
        // that far — it is a plain Win32 layered window, recomposited at its full
        // pixel area every frame whether or not anything is drawn there. Four times
        // the rope's own length already comfortably covers a full-screen drag at the
        // shipped proportions; twelve made the window, and the per-frame compositing
        // cost, roughly nine times larger than this for no reach anyone could reach.
        RopeElasticity.Stretchy => new RopeElasticityPhysics(MaxStretchRatioBonus: 3, MaximumReachRatio: 4),
        _ => new RopeElasticityPhysics(MaxStretchRatioBonus: 0, MaximumReachRatio: 0.98),
    };
}
