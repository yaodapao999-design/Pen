using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public struct SimSample
{
    public float time;
    public Vector3 playerPos;
    public Quaternion playerRot;
    public Vector3 enemyPos;
    public Quaternion enemyRot;
}

public class PredictResult
{
    public bool isKillShot;
    public int killFrame = -1;
    public List<SimSample> samples = new List<SimSample>(256);
}

/// <summary>
/// 出界预判器：独立 PhysicsScene 模拟弹射，预判敌人是否出界
/// 出界判定复用 FallOffCheck
/// </summary>
public class FallOffPredictor : MonoBehaviour
{
    [Header("模拟参数")]
    [SerializeField] private int maxSimSteps = 240;
    [SerializeField] private float simDeltaTime = 1f / 60f;

    [Header("采样参数")]
    [Tooltip("每隔多少模拟帧采样一次轨迹点（1=每帧都采，2=隔一帧采一次）")]
    [SerializeField] private int sampleInterval = 1;

    [Header("擂台引用")]
    [Tooltip("桌子的根物体，会被克隆到模拟场景")]
    [SerializeField] private GameObject table;

    private Scene simScene;
    private PhysicsScene simPhysics;
    private bool isInitialized;

    private GameObject simPlayerGo;
    private Rigidbody simPlayerRb;
    private CapsuleCollider simPlayerCol;

    private GameObject simEnemyGo;
    private Rigidbody simEnemyRb;
    private CapsuleCollider simEnemyCol;

    private GameObject simArenaGo;

    private PenEntity playerPen;
    private PenEntity enemyPen;
    private int playerLayer;
    private int enemyLayer;

    public void Init(PenEntity player, PenEntity enemy)
    {
        playerPen = player;
        enemyPen = enemy;

        // 自动对齐真实物理步长，确保模拟和真实场景时序一致
        simDeltaTime = Time.fixedDeltaTime;

        simScene = SceneManager.CreateScene(
            "__FallOffSim__",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D)
        );
        simPhysics = simScene.GetPhysicsScene();

        playerLayer = player.gameObject.layer;
        enemyLayer = enemy.gameObject.layer;

        simPlayerGo = CreateSimPen("SimPlayer", player, playerLayer);
        simEnemyGo = CreateSimPen("SimEnemy", enemy, enemyLayer);

        simPlayerRb = simPlayerGo.GetComponent<Rigidbody>();
        simPlayerCol = simPlayerGo.GetComponent<CapsuleCollider>();
        simEnemyRb = simEnemyGo.GetComponent<Rigidbody>();
        simEnemyCol = simEnemyGo.GetComponent<CapsuleCollider>();

        if (table != null)
            simArenaGo = CloneArena(table);

        simPlayerGo.SetActive(false);
        simEnemyGo.SetActive(false);

        isInitialized = true;
    }

    public void SyncSimScene()
    {
        if (!isInitialized) return;

        if (simArenaGo != null && table != null)
            SyncArenaTransforms(table.transform, simArenaGo.transform);

        SyncColliderParams(simPlayerCol, playerPen.penCollider);
        SyncColliderParams(simEnemyCol, enemyPen.penCollider);

        simPlayerGo.layer = playerPen.gameObject.layer;
        simEnemyGo.layer = enemyPen.gameObject.layer;
    }

    public PredictResult Predict(BattleContext ctx)
    {
        var result = new PredictResult();
        if (!isInitialized || enemyPen == null) return result;

        simPlayerGo.SetActive(true);
        simEnemyGo.SetActive(true);

        ResetSimBody(simPlayerGo, simPlayerRb, simPlayerCol, playerPen);
        ResetSimBody(simEnemyGo, simEnemyRb, simEnemyCol, enemyPen);

        simPlayerRb.WakeUp();
        simEnemyRb.WakeUp();

        ApplyLaunchForce(simPlayerRb, simPlayerCol, ctx);
        simPhysics.Simulate(simDeltaTime);

        float initialEnemyY = simEnemyRb.worldCenterOfMass.y;
        int offTableFrames = 0;
        int requiredFrames = enemyPen.FallOff.requiredFrames;

        for (int i = 0; i < maxSimSteps; i++)
        {
            simPhysics.Simulate(simDeltaTime);

            if (i % sampleInterval == 0)
            {
                result.samples.Add(new SimSample
                {
                    time = i * simDeltaTime,
                    playerPos = simPlayerRb.position,
                    playerRot = simPlayerRb.rotation,
                    enemyPos = simEnemyRb.position,
                    enemyRot = simEnemyRb.rotation,
                });
            }

            if (FallOff.CheckFallOff(
                    simEnemyRb.worldCenterOfMass, simEnemyRb.linearVelocity, initialEnemyY,
                    enemyPen.FallOff.raycastDistance, enemyPen.FallOff.fallVelocityThreshold,
                    enemyPen.FallOff.tableLayer, simPhysics))
            {
                offTableFrames++;
                if (offTableFrames >= requiredFrames)
                {
                    result.isKillShot = true;
                    result.killFrame = i;
                    break;
                }
            }
            else
            {
                offTableFrames = 0;
            }

            if (i > 60 &&
                simPlayerRb.linearVelocity.sqrMagnitude < 0.0001f &&
                simEnemyRb.linearVelocity.sqrMagnitude < 0.0001f)
            {
                break;
            }
        }

        simPlayerGo.SetActive(false);
        simEnemyGo.SetActive(false);

        return result;
    }

    private void ApplyLaunchForce(Rigidbody simRb, CapsuleCollider simCol, BattleContext ctx)
    {
        // 只清 player 的速度，enemy 已在 ResetSimBody 中同步真实速度
        simRb.linearVelocity = Vector3.zero;
        simRb.angularVelocity = Vector3.zero;

        Vector3 penAxis = simCol.direction switch
        {
            0 => simRb.transform.right,
            1 => simRb.transform.up,
            2 => simRb.transform.forward,
            _ => simRb.transform.right
        };
        float halfHeight = (simCol.height / 2f) - simCol.radius;
        Vector3 forcePosition = simRb.worldCenterOfMass + penAxis * (ctx.ContactOffset * halfHeight);

        Vector3 force = ctx.LaunchDirection * (ctx.pen.baseForce * ctx.LaunchForce);
        simRb.AddForceAtPosition(force, forcePosition, ForceMode.Impulse);
    }

    private GameObject CreateSimPen(string name, PenEntity original, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        SceneManager.MoveGameObjectToScene(go, simScene);

        var rb = go.AddComponent<Rigidbody>();
        var srcRb = original.rb;
        rb.mass = srcRb.mass;
        rb.linearDamping = srcRb.linearDamping;
        rb.angularDamping = srcRb.angularDamping;
        rb.useGravity = srcRb.useGravity;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = srcRb.collisionDetectionMode;
        rb.constraints = srcRb.constraints;

        var srcCol = original.penCollider;
        var col = go.AddComponent<CapsuleCollider>();
        col.center = srcCol.center;
        col.radius = srcCol.radius;
        col.height = srcCol.height;
        col.direction = srcCol.direction;

        if (srcCol.sharedMaterial != null)
            col.sharedMaterial = srcCol.sharedMaterial;

        return go;
    }

    private GameObject CloneArena(GameObject original)
    {
        var clone = Instantiate(original);
        clone.name = "SimArena";
        SceneManager.MoveGameObjectToScene(clone, simScene);

        foreach (var r in clone.GetComponentsInChildren<Renderer>())
            Destroy(r);
        foreach (var mf in clone.GetComponentsInChildren<MeshFilter>())
            Destroy(mf);

        int layer = GetLayerFromMask(enemyPen.FallOff.tableLayer);
        SetLayerRecursive(clone, layer);

        return clone;
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private int GetLayerFromMask(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }
        return 0;
    }

    private void ResetSimBody(GameObject go, Rigidbody simRb, CapsuleCollider simCol, PenEntity original)
    {
        simRb.WakeUp();

        go.transform.SetPositionAndRotation(
            original.transform.position,
            original.transform.rotation
        );

        // 同步真实速度，保留拖拽结束时的残余速度
        simRb.linearVelocity = original.rb.linearVelocity;
        simRb.angularVelocity = original.rb.angularVelocity;

        simRb.mass = original.rb.mass;
        simRb.linearDamping = original.rb.linearDamping;
        simRb.angularDamping = original.rb.angularDamping;
        simRb.useGravity = original.rb.useGravity;
        simRb.collisionDetectionMode = original.rb.collisionDetectionMode;
        simRb.constraints = original.rb.constraints;

        // 同步重心
        if (original.rb.automaticCenterOfMass)
            simRb.ResetCenterOfMass();
        else
            simRb.centerOfMass = original.rb.centerOfMass;

        // 同步转动惯量
        if (original.rb.automaticInertiaTensor)
            simRb.ResetInertiaTensor();
        else
        {
            simRb.inertiaTensor = original.rb.inertiaTensor;
            simRb.inertiaTensorRotation = original.rb.inertiaTensorRotation;
        }

        SyncColliderParams(simCol, original.penCollider);
    }

    private void SyncColliderParams(CapsuleCollider sim, CapsuleCollider src)
    {
        sim.center = src.center;
        sim.radius = src.radius;
        sim.height = src.height;
        sim.direction = src.direction;
        sim.sharedMaterial = src.sharedMaterial;
    }

    private void SyncArenaTransforms(Transform src, Transform dst)
    {
        dst.SetPositionAndRotation(src.position, src.rotation);
        dst.localScale = src.localScale;

        int count = Mathf.Min(src.childCount, dst.childCount);
        for (int i = 0; i < count; i++)
            SyncArenaTransforms(src.GetChild(i), dst.GetChild(i));
    }

    private void OnDestroy()
    {
        if (isInitialized && simScene.IsValid())
        {
            if (simPlayerGo != null) Destroy(simPlayerGo);
            if (simEnemyGo != null) Destroy(simEnemyGo);
            if (simArenaGo != null) Destroy(simArenaGo);
            SceneManager.UnloadSceneAsync(simScene);
        }
    }
}