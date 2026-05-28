using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Display-only trajectory shaping for the launch arrow.
/// It never changes the physics prediction source; it only converts raw dragged-point samples into a readable,
/// low-poly-friendly path: smooth enough to aim with, bounded enough not to knot.
/// </summary>
public sealed class AimTrajectoryDisplayFilter
{
    public struct Settings
    {
        public bool Smooth;
        public int SmoothingPasses;
        public float CornerCut;

        public bool LimitBend;
        public float MaxTotalBendDegrees;
        public float MaxTurnPerSegmentDegrees;

        public bool TemporalLerp;
        public bool ResampleForStability;
        public int StableSampleCount;
        public float TrajectoryLerpSpeed;
        public float TrajectoryLerpResetDistance;
        public float TrajectoryLerpResetAngle;

        public float FullPullStabilityThreshold;
        public float FullPullTrajectoryLerpSpeed;
        public float FullPullShapeDeadbandDistance;
        public float FullPullDirectionDeadbandDegrees;
        public float FullPullArcLengthDeadband;
    }

    private readonly List<Vector3> _readable = new List<Vector3>(64);
    private readonly List<Vector3> _scratch = new List<Vector3>(128);
    private readonly List<Vector3> _temporal = new List<Vector3>(128);
    private readonly List<Vector3> _temporalScratch = new List<Vector3>(128);
    private bool _hasTemporal;

    public void Reset()
    {
        _hasTemporal = false;
        _temporal.Clear();
        _temporalScratch.Clear();
    }

    public IReadOnlyList<Vector3> Build(
        IReadOnlyList<Vector3> raw,
        float force,
        float deltaTime,
        Settings settings)
    {
        CopyTrajectory(raw, _readable);
        if (_readable.Count == 0)
        {
            Reset();
            return _readable;
        }

        if (_readable.Count >= 3 &&
            settings.Smooth &&
            settings.SmoothingPasses > 0 &&
            settings.CornerCut > 0.001f)
        {
            int passes = Mathf.Clamp(settings.SmoothingPasses, 0, 3);
            float cut = Mathf.Clamp(settings.CornerCut, 0.02f, 0.32f);
            for (int pass = 0; pass < passes; pass++)
                SmoothTrajectoryOnce(_readable, _scratch, cut);
        }

        if (_readable.Count >= 3 &&
            settings.LimitBend &&
            settings.MaxTotalBendDegrees > 0.001f)
        {
            LimitDisplayedBend(
                _readable,
                _scratch,
                settings.MaxTotalBendDegrees,
                settings.MaxTurnPerSegmentDegrees);
        }

        if (_readable.Count >= 2 &&
            settings.ResampleForStability &&
            settings.StableSampleCount >= 3)
        {
            ResampleTrajectory(_readable, _scratch, settings.StableSampleCount);
        }

        ApplyTemporalLerp(_readable, force, deltaTime, settings);

        if (_temporal.Count >= 3 &&
            settings.LimitBend &&
            settings.MaxTotalBendDegrees > 0.001f)
        {
            LimitDisplayedBend(
                _temporal,
                _scratch,
                settings.MaxTotalBendDegrees,
                settings.MaxTurnPerSegmentDegrees);
        }

        return _temporal;
    }

    public static float MeasureFlatArcLength(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 a = points[i - 1]; a.y = 0f;
            Vector3 b = points[i]; b.y = 0f;
            total += Vector3.Distance(a, b);
        }
        return total;
    }

    public static float MeasureTotalAbsoluteTurn(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return 0f;

        if (!TryFindInitialFlatDirection(points, out Vector3 prevDir))
            return 0f;

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 dir = points[i] - points[i - 1];
            dir.y = 0f;
            if (dir.sqrMagnitude <= 1e-6f)
                continue;

            dir.Normalize();
            total += Mathf.Abs(Vector3.SignedAngle(prevDir, dir, Vector3.up));
            prevDir = dir;
        }

        return total;
    }

    public static float MeasureMaxSegmentTurn(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return 0f;

        if (!TryFindInitialFlatDirection(points, out Vector3 prevDir))
            return 0f;

        float maxTurn = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 dir = points[i] - points[i - 1];
            dir.y = 0f;
            if (dir.sqrMagnitude <= 1e-6f)
                continue;

            dir.Normalize();
            maxTurn = Mathf.Max(maxTurn, Mathf.Abs(Vector3.SignedAngle(prevDir, dir, Vector3.up)));
            prevDir = dir;
        }

        return maxTurn;
    }

    private void ApplyTemporalLerp(
        IReadOnlyList<Vector3> current,
        float force,
        float deltaTime,
        Settings settings)
    {
        if (current == null || current.Count == 0)
        {
            Reset();
            return;
        }

        if (!settings.TemporalLerp ||
            settings.TrajectoryLerpSpeed <= 0.001f ||
            deltaTime <= 0f ||
            current.Count < 2)
        {
            CopyTrajectory(current, _temporal);
            _hasTemporal = current.Count >= 2;
            return;
        }

        if (!_hasTemporal || _temporal.Count < 2 || ShouldResetTemporal(current, settings))
        {
            CopyTrajectory(current, _temporal);
            _hasTemporal = true;
            return;
        }

        bool fullPull = IsFullPull(force, settings);
        float currentArc = MeasureFlatArcLength(current);
        if (fullPull && ShouldHoldFullPullShape(current, currentArc, settings))
        {
            MoveTemporalStartTo(current[0]);
            _hasTemporal = true;
            return;
        }

        float speed = fullPull
            ? Mathf.Max(1f, settings.FullPullTrajectoryLerpSpeed)
            : Mathf.Max(1f, settings.TrajectoryLerpSpeed);
        float follow = 1f - Mathf.Exp(-speed * deltaTime);
        float previousArc = MeasureFlatArcLength(_temporal);
        int lastIndex = current.Count - 1;

        _temporalScratch.Clear();
        for (int i = 0; i < current.Count; i++)
        {
            Vector3 target = current[i];
            if (i == 0)
            {
                _temporalScratch.Add(target);
                continue;
            }

            float normalizedArc = lastIndex > 0 ? i / (float)lastIndex : 0f;
            Vector3 previous = SampleFlatPolylineNormalized(_temporal, previousArc, normalizedArc);
            Vector3 smoothed = Vector3.Lerp(previous, target, follow);
            smoothed.y = target.y;
            _temporalScratch.Add(smoothed);
        }

        CopyTrajectory(_temporalScratch, _temporal);
        _hasTemporal = true;
    }

    private bool ShouldHoldFullPullShape(
        IReadOnlyList<Vector3> current,
        float currentArc,
        Settings settings)
    {
        if (current == null || current.Count < 2 || _temporal.Count < 2)
            return false;

        float previousArc = MeasureFlatArcLength(_temporal);
        float arcDeadband = Mathf.Max(0f, settings.FullPullArcLengthDeadband);
        if (Mathf.Abs(currentArc - previousArc) > arcDeadband)
            return false;

        if (!TryFindInitialFlatDirection(current, out Vector3 currentDir) ||
            !TryFindInitialFlatDirection(_temporal, out Vector3 previousDir))
            return false;

        float directionDeadband = Mathf.Clamp(settings.FullPullDirectionDeadbandDegrees, 0f, 45f);
        if (Vector3.Angle(previousDir, currentDir) > directionDeadband)
            return false;

        float shapeDeadband = Mathf.Max(0f, settings.FullPullShapeDeadbandDistance);
        if (shapeDeadband <= 0.0001f)
            return false;

        Vector3 currentStart = current[0];
        Vector3 previousStart = _temporal[0];
        currentStart.y = 0f;
        previousStart.y = 0f;
        Vector3 startDelta = currentStart - previousStart;

        const int SAMPLE_COUNT = 6;
        for (int i = 1; i < SAMPLE_COUNT; i++)
        {
            float t = i / (float)(SAMPLE_COUNT - 1);
            Vector3 currentSample = SampleFlatPolylineNormalized(current, currentArc, t);
            Vector3 previousSample = SampleFlatPolylineNormalized(_temporal, previousArc, t) + startDelta;
            currentSample.y = 0f;
            previousSample.y = 0f;
            if (Vector3.Distance(currentSample, previousSample) > shapeDeadband)
                return false;
        }

        return true;
    }

    private void MoveTemporalStartTo(Vector3 start)
    {
        if (_temporal.Count == 0)
            return;

        Vector3 delta = start - _temporal[0];
        for (int i = 0; i < _temporal.Count; i++)
            _temporal[i] += delta;
        _temporal[0] = start;
    }

    private bool ShouldResetTemporal(IReadOnlyList<Vector3> current, Settings settings)
    {
        if (current == null || current.Count < 2 || _temporal.Count < 2)
            return true;

        Vector3 currentStart = current[0];
        Vector3 previousStart = _temporal[0];
        currentStart.y = 0f;
        previousStart.y = 0f;
        if (Vector3.Distance(currentStart, previousStart) > Mathf.Max(0.01f, settings.TrajectoryLerpResetDistance))
            return true;

        if (!TryFindInitialFlatDirection(current, out Vector3 currentDir) ||
            !TryFindInitialFlatDirection(_temporal, out Vector3 previousDir))
            return true;

        float angle = Vector3.Angle(previousDir, currentDir);
        return angle > Mathf.Clamp(settings.TrajectoryLerpResetAngle, 1f, 179f);
    }

    private static bool IsFullPull(float force, Settings settings)
    {
        return force >= Mathf.Clamp01(settings.FullPullStabilityThreshold);
    }

    private static void SmoothTrajectoryOnce(List<Vector3> points, List<Vector3> scratch, float cut)
    {
        if (points == null || scratch == null || points.Count < 3)
            return;

        scratch.Clear();
        scratch.Add(points[0]);

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            scratch.Add(Vector3.Lerp(a, b, cut));
            scratch.Add(Vector3.Lerp(a, b, 1f - cut));
        }

        scratch.Add(points[points.Count - 1]);

        CopyTrajectory(scratch, points);
    }

    private static void ResampleTrajectory(List<Vector3> points, List<Vector3> scratch, int sampleCount)
    {
        if (points == null || scratch == null || points.Count < 2)
            return;

        float totalArc = MeasureFlatArcLength(points);
        if (totalArc <= 1e-5f)
            return;

        int count = Mathf.Clamp(sampleCount, 3, 96);
        scratch.Clear();
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            scratch.Add(SampleFlatPolylineNormalized(points, totalArc, t));
        }

        scratch[0] = points[0];
        scratch[scratch.Count - 1] = points[points.Count - 1];
        CopyTrajectory(scratch, points);
    }

    private static void LimitDisplayedBend(
        List<Vector3> points,
        List<Vector3> rawCopy,
        float maxDegrees,
        float maxStepDegrees)
    {
        if (points == null || rawCopy == null || points.Count < 3)
            return;

        CopyTrajectory(points, rawCopy);

        if (!TryFindInitialFlatDirection(rawCopy, out Vector3 initialDir))
            return;

        float maxTurn = Mathf.Clamp(Mathf.Abs(maxDegrees), 1f, 360f);
        float maxStepTurn = Mathf.Clamp(Mathf.Abs(maxStepDegrees), 1f, 89f);
        Vector3 prevRawDir = initialDir;
        float desiredTurn = 0f;
        float displayTurn = 0f;
        float usedBend = 0f;

        points[0] = rawCopy[0];
        for (int i = 1; i < rawCopy.Count; i++)
        {
            Vector3 rawSegment = rawCopy[i] - rawCopy[i - 1];
            Vector3 flatSegment = rawSegment;
            flatSegment.y = 0f;
            float segmentLength = flatSegment.magnitude;
            if (segmentLength <= 1e-5f)
            {
                Vector3 held = points[i - 1];
                held.y = rawCopy[i].y;
                points[i] = held;
                continue;
            }

            Vector3 rawDir = flatSegment / segmentLength;
            float rawTurn = Vector3.SignedAngle(prevRawDir, rawDir, Vector3.up);
            desiredTurn += rawTurn;

            float turnTowardRaw = desiredTurn - displayTurn;
            float remainingBend = maxTurn - usedBend;
            if (Mathf.Abs(turnTowardRaw) > 0.001f && remainingBend > 0.001f)
            {
                float stepTurn = Mathf.Clamp(turnTowardRaw, -maxStepTurn, maxStepTurn);
                if (Mathf.Abs(stepTurn) > remainingBend)
                    stepTurn = Mathf.Sign(stepTurn) * remainingBend;

                displayTurn += stepTurn;
                usedBend += Mathf.Abs(stepTurn);
            }

            Vector3 displayDir = RotateAroundUp(initialDir, displayTurn);
            Vector3 next = points[i - 1] + displayDir.normalized * segmentLength;
            next.y = rawCopy[i].y;
            points[i] = next;
            prevRawDir = rawDir;
        }
    }

    private static bool TryFindInitialFlatDirection(IReadOnlyList<Vector3> points, out Vector3 direction)
    {
        for (int i = 1; i < points.Count; i++)
        {
            direction = points[i] - points[i - 1];
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                direction.Normalize();
                return true;
            }
        }

        direction = Vector3.zero;
        return false;
    }

    private static Vector3 RotateAroundUp(Vector3 direction, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector3(
            direction.x * cos + direction.z * sin,
            0f,
            -direction.x * sin + direction.z * cos);
    }

    private static void CopyTrajectory(IReadOnlyList<Vector3> source, List<Vector3> target)
    {
        target.Clear();
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);
    }

    private static Vector3 SampleFlatPolylineNormalized(IReadOnlyList<Vector3> points, float totalArc, float t)
    {
        if (points == null || points.Count == 0)
            return Vector3.zero;
        if (points.Count == 1 || totalArc <= 1e-5f)
            return points[Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 1)];

        float targetArc = Mathf.Clamp01(t) * totalArc;
        float walked = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 aFlat = points[i - 1]; aFlat.y = 0f;
            Vector3 bFlat = points[i]; bFlat.y = 0f;
            float segment = Vector3.Distance(aFlat, bFlat);
            if (segment <= 1e-5f)
                continue;

            if (walked + segment >= targetArc)
            {
                float localT = (targetArc - walked) / segment;
                return Vector3.Lerp(points[i - 1], points[i], localT);
            }

            walked += segment;
        }

        return points[points.Count - 1];
    }
}
