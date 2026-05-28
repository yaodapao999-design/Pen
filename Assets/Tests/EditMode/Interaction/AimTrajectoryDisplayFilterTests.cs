using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class AimTrajectoryDisplayFilterTests
{
    [Test]
    public void Build_KeepsFirstPointLockedToDraggedContact()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var raw = new List<Vector3>
        {
            new Vector3(2f, 0.4f, -1f),
            new Vector3(3f, 0.4f, -1f),
            new Vector3(4f, 0.4f, -0.5f)
        };

        var output = filter.Build(raw, 0.5f, 0.016f, DefaultSettings());

        Assert.That(Vector3.Distance(output[0], raw[0]), Is.LessThan(0.0001f));
    }

    [Test]
    public void Build_ClampsTotalAbsoluteBendForSShape()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.TemporalLerp = false;
        settings.MaxTotalBendDegrees = 90f;
        settings.MaxTurnPerSegmentDegrees = 12f;

        var raw = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 1f),
            new Vector3(2f, 0f, 1f),
            new Vector3(2f, 0f, 2f),
            new Vector3(3f, 0f, 2f),
            new Vector3(3f, 0f, 3f)
        };

        var output = filter.Build(raw, 1f, 0.016f, settings);

        Assert.That(AimTrajectoryDisplayFilter.MeasureTotalAbsoluteTurn(output), Is.LessThanOrEqualTo(90.1f));
        Assert.That(AimTrajectoryDisplayFilter.MeasureMaxSegmentTurn(output), Is.LessThanOrEqualTo(12.1f));
    }

    [Test]
    public void Build_LerpsTrajectoryWithoutMovingStartOrOvershooting()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.Smooth = false;
        settings.LimitBend = false;
        settings.ResampleForStability = false;
        settings.TrajectoryLerpSpeed = 8f;

        var previous = StraightPath(z: 0f);
        var target = StraightPath(z: 1f);

        filter.Build(previous, 0.5f, 0.016f, settings);
        var output = filter.Build(target, 0.5f, 0.016f, settings);

        Assert.That(Vector3.Distance(output[0], target[0]), Is.LessThan(0.0001f));
        Assert.That(output[2].z, Is.GreaterThan(0f));
        Assert.That(output[2].z, Is.LessThan(1f));
    }

    [Test]
    public void Build_FullPullUsesSlowerShapeFollow()
    {
        var normalFilter = new AimTrajectoryDisplayFilter();
        var fullPullFilter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.Smooth = false;
        settings.LimitBend = false;
        settings.ResampleForStability = false;
        settings.TrajectoryLerpSpeed = 20f;
        settings.FullPullTrajectoryLerpSpeed = 4f;
        settings.FullPullStabilityThreshold = 0.96f;
        settings.FullPullShapeDeadbandDistance = 0f;
        settings.FullPullArcLengthDeadband = 0f;

        var previous = StraightPath(z: 0f);
        var target = StraightPath(z: 1f);

        normalFilter.Build(previous, 0.5f, 0.016f, settings);
        fullPullFilter.Build(previous, 1f, 0.016f, settings);

        var normal = normalFilter.Build(target, 0.5f, 0.016f, settings);
        var fullPull = fullPullFilter.Build(target, 1f, 0.016f, settings);

        Assert.That(fullPull[2].z, Is.LessThan(normal[2].z));
    }

    [Test]
    public void Build_FullPullSmallNoiseKeepsPreviousShape()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.Smooth = false;
        settings.LimitBend = false;
        settings.ResampleForStability = false;
        settings.FullPullStabilityThreshold = 0.96f;
        settings.FullPullShapeDeadbandDistance = 0.10f;
        settings.FullPullDirectionDeadbandDegrees = 6f;
        settings.FullPullArcLengthDeadband = 0.16f;

        var previous = StraightPath(z: 0f);
        var noisy = StraightPath(z: 0.04f);

        filter.Build(previous, 1f, 0.016f, settings);
        var output = filter.Build(noisy, 1f, 0.016f, settings);

        Assert.That(Vector3.Distance(output[0], noisy[0]), Is.LessThan(0.0001f));
        Assert.That(output[2].z, Is.LessThan(0.001f));
    }

    [Test]
    public void Build_FullPullSmallStartDriftMovesStableShapeToDraggedPoint()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.Smooth = false;
        settings.LimitBend = false;
        settings.ResampleForStability = false;
        settings.FullPullShapeDeadbandDistance = 0.10f;
        settings.FullPullDirectionDeadbandDegrees = 6f;
        settings.FullPullArcLengthDeadband = 0.16f;

        var previous = StraightPath(z: 0f);
        var noisy = new List<Vector3>
        {
            new Vector3(0.03f, 0f, 0.02f),
            new Vector3(1.03f, 0f, 0.04f),
            new Vector3(2.03f, 0f, 0.04f),
            new Vector3(3.03f, 0f, 0.04f)
        };

        filter.Build(previous, 1f, 0.016f, settings);
        var output = filter.Build(noisy, 1f, 0.016f, settings);

        Assert.That(Vector3.Distance(output[0], noisy[0]), Is.LessThan(0.0001f));
        Assert.That(output[2].x, Is.EqualTo(2.03f).Within(0.0001f));
        Assert.That(output[2].z, Is.EqualTo(0.02f).Within(0.0001f));
    }

    [Test]
    public void Build_ResetsTemporalLerpWhenDraggedPointJumps()
    {
        var filter = new AimTrajectoryDisplayFilter();
        var settings = DefaultSettings();
        settings.Smooth = false;
        settings.LimitBend = false;
        settings.ResampleForStability = false;
        settings.TrajectoryLerpResetDistance = 0.25f;

        var previous = StraightPath(z: 0f);
        var jumped = new List<Vector3>
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(2f, 0f, 1f),
            new Vector3(3f, 0f, 1f)
        };

        filter.Build(previous, 0.5f, 0.016f, settings);
        var output = filter.Build(jumped, 0.5f, 0.016f, settings);

        Assert.That(Vector3.Distance(output[0], jumped[0]), Is.LessThan(0.0001f));
        Assert.That(Vector3.Distance(output[1], jumped[1]), Is.LessThan(0.0001f));
    }

    private static AimTrajectoryDisplayFilter.Settings DefaultSettings()
    {
        return new AimTrajectoryDisplayFilter.Settings
        {
            Smooth = true,
            SmoothingPasses = 1,
            CornerCut = 0.16f,
            LimitBend = true,
            MaxTotalBendDegrees = 180f,
            MaxTurnPerSegmentDegrees = 12f,
            TemporalLerp = true,
            ResampleForStability = true,
            StableSampleCount = 32,
            TrajectoryLerpSpeed = 18f,
            TrajectoryLerpResetDistance = 0.28f,
            TrajectoryLerpResetAngle = 75f,
            FullPullStabilityThreshold = 0.96f,
            FullPullTrajectoryLerpSpeed = 4f,
            FullPullShapeDeadbandDistance = 0.10f,
            FullPullDirectionDeadbandDegrees = 6f,
            FullPullArcLengthDeadband = 0.16f
        };
    }

    private static List<Vector3> StraightPath(float z)
    {
        return new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, z),
            new Vector3(2f, 0f, z),
            new Vector3(3f, 0f, z)
        };
    }
}
