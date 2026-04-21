using UnityEngine;

/// <summary>
/// 按 DisclosureLevel 将 TrajectorySample 裁剪为可渲染的 DisclosedTrajectory。
/// 纯函数语义;入参 `output` 由调用方复用以避免 GC。
///
/// - Minimal:空披露
/// - Trend:方向带 + 玩家 ghost(最终位姿)
/// - OnCollide:方向带 + 折线(截到 CollisionTime 或全程)+ 玩家 ghost(碰撞瞬间 / 未碰则最终)
/// - Full:方向带 + 完整折线 + 玩家 ghost(最终)+ 敌方 ghost(最终)
/// </summary>
public static class TrajectoryDisclosureFilter
{
    [System.Serializable]
    public struct FilterConfig
    {
        [Tooltip("Trend 级别下,方向指示带长度相对初速度大小的缩放系数(米/(m/s))")]
        public float TrendDirectionMetersPerSpeed;

        [Tooltip("Trend 级别下方向带的硬长度上限(米),防高速下穿屏")]
        public float TrendDirectionMaxLength;

        [Tooltip("Full/OnCollide 级别下对 Sample 点列的二次抽稀步长(1 = 不抽稀)")]
        public int FullDecimation;

        public static FilterConfig Default => new FilterConfig
        {
            TrendDirectionMetersPerSpeed = 0.06f,
            TrendDirectionMaxLength = 0.9f,
            FullDecimation = 1
        };
    }

    public static void Filter(
        TrajectorySample sample,
        DisclosureLevel level,
        in FilterConfig config,
        DisclosedTrajectory output)
    {
        if (output == null) return;
        output.Reset(level);
        if (sample == null || sample.Count <= 0) return;

        switch (level)
        {
            case DisclosureLevel.Minimal:
                break;

            case DisclosureLevel.Trend:
                ApplyDirectionBand(sample, config, output);
                AssignPlayerGhost(sample.FinalPosition, sample.FinalRotation, sample, output);
                break;

            case DisclosureLevel.OnCollide:
                FillOnCollide(sample, config, output);
                break;

            case DisclosureLevel.Full:
                FillFull(sample, config, output);
                break;
        }
    }

    private static void ApplyDirectionBand(TrajectorySample sample, in FilterConfig cfg, DisclosedTrajectory output)
    {
        Vector3 start = sample.InitialPosition;
        Vector3 initialV = sample.InitialVelocityHint;
        float speed = initialV.magnitude;
        float bandLength = Mathf.Min(speed * Mathf.Max(cfg.TrendDirectionMetersPerSpeed, 0f),
                                     Mathf.Max(cfg.TrendDirectionMaxLength, 0f));
        if (bandLength > 1e-4f && speed > 1e-4f)
        {
            output.ShowDirectionBand = true;
            output.DirectionBandStart = start;
            output.DirectionBandEnd = start + initialV.normalized * bandLength;
        }
    }

    private static void FillOnCollide(TrajectorySample sample, in FilterConfig cfg, DisclosedTrajectory output)
    {
        ApplyDirectionBand(sample, cfg, output);

        // 折线截断到 CollisionTime(若无碰撞则全程,表现退化为 Full 玩家侧)
        int stride = Mathf.Max(1, cfg.FullDecimation);
        var srcP = sample.Positions;
        var srcR = sample.Rotations;
        var srcT = sample.Times;
        var dst = output.PathPoints;
        var dstRot = output.PathRotations;

        float cutoffT = sample.CollisionOccurred ? sample.CollisionTime : float.MaxValue;
        for (int i = 0; i < srcP.Count; i += stride)
        {
            if (srcT[i] > cutoffT) break;
            dst.Add(srcP[i]);
            dstRot.Add(srcR[i]);
        }

        if (sample.CollisionOccurred)
        {
            // 末点兜底:碰撞瞬间位姿
            if (dst.Count == 0 || dst[dst.Count - 1] != sample.CollisionPlayerPosition)
            {
                dst.Add(sample.CollisionPlayerPosition);
                dstRot.Add(sample.CollisionPlayerRotation);
            }
            AssignPlayerGhost(sample.CollisionPlayerPosition, sample.CollisionPlayerRotation, sample, output);
        }
        else
        {
            // 未碰撞:兜底末点 + 玩家 ghost 放最终位姿
            int lastIdx = srcP.Count - 1;
            if (lastIdx >= 0 && (dst.Count == 0 || dst[dst.Count - 1] != srcP[lastIdx]))
            {
                dst.Add(srcP[lastIdx]);
                dstRot.Add(srcR[lastIdx]);
            }
            AssignPlayerGhost(sample.FinalPosition, sample.FinalRotation, sample, output);
        }

        output.ShowFullPath = dst.Count >= 2;
        output.ShowPose = dstRot.Count >= 2;
        // OnCollide 不显示敌方 ghost
    }

    private static void FillFull(TrajectorySample sample, in FilterConfig cfg, DisclosedTrajectory output)
    {
        ApplyDirectionBand(sample, cfg, output);

        int stride = Mathf.Max(1, cfg.FullDecimation);
        var src = sample.Positions;
        var srcRot = sample.Rotations;
        var dst = output.PathPoints;
        var dstRot = output.PathRotations;

        for (int i = 0; i < src.Count; i += stride)
        {
            dst.Add(src[i]);
            dstRot.Add(srcRot[i]);
        }
        int lastIdx = src.Count - 1;
        if (lastIdx >= 0 && (dst.Count == 0 || dst[dst.Count - 1] != src[lastIdx]))
        {
            dst.Add(src[lastIdx]);
            dstRot.Add(srcRot[lastIdx]);
        }

        output.ShowFullPath = dst.Count >= 2;
        output.ShowPose = dstRot.Count >= 2;

        AssignPlayerGhost(sample.FinalPosition, sample.FinalRotation, sample, output);

        if (sample.HasEnemy && sample.StopReason != TrajectoryStopReason.NotStarted)
        {
            output.ShowEnemyGhost = true;
            output.EnemyGhostPosition = sample.FinalEnemyPosition;
            output.EnemyGhostRotation = sample.FinalEnemyRotation;
        }
    }

    private static void AssignPlayerGhost(Vector3 pos, Quaternion rot, TrajectorySample sample, DisclosedTrajectory output)
    {
        if (sample.StopReason == TrajectoryStopReason.NotStarted) return;
        output.ShowGhostPen = true;
        output.GhostPenPosition = pos;
        output.GhostPenRotation = rot;
    }
}
