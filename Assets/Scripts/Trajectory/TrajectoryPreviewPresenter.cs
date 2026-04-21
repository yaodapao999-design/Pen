using UnityEngine;

/// <summary>
/// 表现层:消费 DisclosedTrajectory 渲染方向带 / 玩家 ghost / 敌方 ghost / 完整折线。
/// 不含任何物理或过滤逻辑。
///
/// Ghost pen 在首次需要显示时 lazy 克隆 `sourcePenForGhost.gameObject` / `sourceEnemyPenForGhost.gameObject`:
///   - **不删除**任何组件(主场景笔上挂有 [RequireComponent] 依赖链,硬删会失败)
///   - 所有 MonoBehaviour 设 `enabled = false`;Rigidbody 设 kinematic + 无重力 + FreezeAll + 不检测碰撞;Collider 设 enabled = false
///   - 所有 MeshRenderer 的材质替换为 `ghostMaterial`(未配时 runtime 创建 URP Unlit 透明白色,alpha 0.3)
///   - 挂在本 Presenter Transform 下,默认 SetActive(false)
/// 装配变化时**不自动重建 ghost**(首版限制,非目标范围)。
/// </summary>
public class TrajectoryPreviewPresenter : MonoBehaviour
{
    [Header("Trend (D1) 视觉")]
    [SerializeField] private LineRenderer directionBandRenderer;

    [Header("Ghost Pen(D1 / OnCollide / Full 共享)")]
    [Tooltip("玩家 Ghost pen 克隆模板;须指向主场景玩家笔")]
    [SerializeField] private PenEntity sourcePenForGhost;

    [Tooltip("敌方 Ghost pen 克隆模板;Full 级别显示敌方被撞后的最终位姿。为空则 Full 不显示敌方 ghost")]
    [SerializeField] private PenEntity sourceEnemyPenForGhost;

    [Tooltip("Ghost pen 使用的半透明材质;为空时 Presenter 运行时自动创建一个 URP Unlit 透明白色(alpha 0.3)作为共享")]
    [SerializeField] private Material ghostMaterial;

    [Header("Full (D2) 视觉")]
    [SerializeField] private LineRenderer fullPathRenderer;

    [Header("通用")]
    [SerializeField, Min(0f)] private float lineWidth = 0.03f;

    private GameObject _playerGhost;
    private GameObject _enemyGhost;
    private bool _playerGhostBuildAttempted;
    private bool _enemyGhostBuildAttempted;

    private void Awake()
    {
        if (directionBandRenderer != null) directionBandRenderer.widthMultiplier = lineWidth;
        if (fullPathRenderer != null) fullPathRenderer.widthMultiplier = lineWidth;
        HideAll();
    }

    private void OnDisable() => HideAll();

    public void Show(DisclosedTrajectory disclosed)
    {
        if (disclosed == null || disclosed.Level == DisclosureLevel.Minimal)
        {
            HideAll();
            return;
        }

        ApplyDirectionBand(disclosed);
        ApplyPlayerGhost(disclosed);
        ApplyEnemyGhost(disclosed);
        ApplyFullPath(disclosed);
    }

    public void Hide() => HideAll();

    private void ApplyDirectionBand(DisclosedTrajectory d)
    {
        if (directionBandRenderer == null) return;
        if (d.ShowDirectionBand)
        {
            directionBandRenderer.enabled = true;
            directionBandRenderer.positionCount = 2;
            directionBandRenderer.SetPosition(0, d.DirectionBandStart);
            directionBandRenderer.SetPosition(1, d.DirectionBandEnd);
        }
        else
        {
            directionBandRenderer.enabled = false;
        }
    }

    private void ApplyPlayerGhost(DisclosedTrajectory d)
    {
        if (d.ShowGhostPen)
        {
            EnsurePlayerGhost();
            if (_playerGhost == null) return;
            _playerGhost.transform.SetPositionAndRotation(d.GhostPenPosition, d.GhostPenRotation);
            if (!_playerGhost.activeSelf) _playerGhost.SetActive(true);
        }
        else if (_playerGhost != null && _playerGhost.activeSelf)
        {
            _playerGhost.SetActive(false);
        }
    }

    private void ApplyEnemyGhost(DisclosedTrajectory d)
    {
        if (d.ShowEnemyGhost)
        {
            EnsureEnemyGhost();
            if (_enemyGhost == null) return;
            _enemyGhost.transform.SetPositionAndRotation(d.EnemyGhostPosition, d.EnemyGhostRotation);
            if (!_enemyGhost.activeSelf) _enemyGhost.SetActive(true);
        }
        else if (_enemyGhost != null && _enemyGhost.activeSelf)
        {
            _enemyGhost.SetActive(false);
        }
    }

    private void ApplyFullPath(DisclosedTrajectory d)
    {
        if (fullPathRenderer == null) return;
        if (d.ShowFullPath && d.PathPoints != null && d.PathPoints.Count >= 2)
        {
            fullPathRenderer.enabled = true;
            var pts = d.PathPoints;
            fullPathRenderer.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
                fullPathRenderer.SetPosition(i, pts[i]);
        }
        else
        {
            fullPathRenderer.enabled = false;
        }
    }

    private void HideAll()
    {
        if (directionBandRenderer != null) directionBandRenderer.enabled = false;
        if (fullPathRenderer != null) fullPathRenderer.enabled = false;
        if (_playerGhost != null && _playerGhost.activeSelf) _playerGhost.SetActive(false);
        if (_enemyGhost != null && _enemyGhost.activeSelf) _enemyGhost.SetActive(false);
    }

    private void EnsurePlayerGhost()
    {
        if (_playerGhost != null) return;
        _playerGhost = TryBuildGhost(sourcePenForGhost, "PlayerGhost", ref _playerGhostBuildAttempted);
    }

    private void EnsureEnemyGhost()
    {
        if (_enemyGhost != null) return;
        _enemyGhost = TryBuildGhost(sourceEnemyPenForGhost, "EnemyGhost", ref _enemyGhostBuildAttempted);
    }

    private GameObject TryBuildGhost(PenEntity source, string ghostName, ref bool buildAttemptedFlag)
    {
        if (source == null)
        {
            if (!buildAttemptedFlag)
                Debug.LogWarning($"[TrajectoryPreviewPresenter] {ghostName} 模板 (PenEntity) 未赋值,ghost 不构建");
            buildAttemptedFlag = true;
            return null;
        }

        // 装配未就绪(场景加载到 BattleStateMachine.Start/TryEnsureEnemyAssembly 之间的窗口)→ 延后
        if (source.Assembly == null || source.Assembly.BarrelData == null)
            return null;

        var go = Instantiate(source.gameObject);
        go.name = ghostName;
        go.SetActive(false);

        // 不删除任何组件,一律禁用:避开 [RequireComponent] 依赖链;无物理、无脚本副作用
        var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < mbs.Length; i++)
            if (mbs[i] != null) mbs[i].enabled = false;
        var rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            if (rbs[i] == null) continue;
            rbs[i].isKinematic = true;
            rbs[i].useGravity = false;
            rbs[i].constraints = RigidbodyConstraints.FreezeAll;
            rbs[i].detectCollisions = false;
        }
        var cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].enabled = false;

        var mat = GetOrCreateGhostMaterial();
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = new Material[mr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }

        go.transform.SetParent(transform, worldPositionStays: false);
        buildAttemptedFlag = true;
        return go;
    }

    private Material GetOrCreateGhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = "GhostPenMaterial(runtime)" };

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        var c = new Color(1f, 1f, 1f, 0.3f);
        mat.color = c;
        mat.SetColor("_BaseColor", c);
        ghostMaterial = mat;
        return ghostMaterial;
    }
}
