using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 改装场景总控制器
/// 进入改装时禁用 CinemachineBrain，直接 Lerp 相机到抽屉视角
/// 退出时还原 Brain，战斗镜头控制完全不受影响
/// </summary>
public class WorkshopController : MonoBehaviour
{
    [Header("抽屉视角目标")]
    public Transform DrawerCamTarget;      // 空 GameObject，摆到抽屉视角位置和朝向
    public float TransitionDuration = 1.2f;

    [Header("抽屉")]
    public Transform DrawerTransform;      // 抽屉模型 Transform
    public float DrawerOpenOffset = 0.3f;  // Z 轴打开距离
    public float DrawerOpenDuration = 0.5f;

    [Header("引用")]
    public PenAssembly WorkshopAssembly;
    public PenPartDatabase PartDatabase;
    public WorkshopPenSpawner PenSpawner;

    [Header("存档")]
    public PenBuildData CurrentBuild;

    private Camera _cam;
    private CinemachineBrain _brain;
    private bool _inDrawer;

    private void Awake()
    {
        _cam = Camera.main;
        _brain = _cam.GetComponent<CinemachineBrain>();
    }

    private void Start()
    {
        if (CurrentBuild != null && WorkshopAssembly != null
            && !string.IsNullOrEmpty(CurrentBuild.BarrelID))
            CurrentBuild.ApplyTo(WorkshopAssembly, PartDatabase);
    }

    /// <summary>战斗结束后调用，镜头 Lerp 到抽屉并自动开抽屉</summary>
    public void EnterWorkshop()
    {
        if (_inDrawer) return;
        _inDrawer = true;
        StartCoroutine(TransitionToDrawer());
    }

    /// <summary>改装完成，保存存档，镜头 Lerp 回战斗位置</summary>
    public void FinishWorkshop()
    {
        if (!_inDrawer) return;
        _inDrawer = false;
        CurrentBuild = PenBuildData.FromAssembly(WorkshopAssembly);
        PenSpawner?.ClearParts();
        StartCoroutine(TransitionToBattle());
    }

    private IEnumerator TransitionToDrawer()
    {
        if (_brain != null) _brain.enabled = false;

        yield return MoveCam(DrawerCamTarget);

        // 开抽屉（Z 轴位移）+ 生成零件
        if (DrawerTransform != null)
            yield return MoveDrawer(DrawerTransform.localPosition.z + DrawerOpenOffset);

        PenSpawner?.SpawnParts();
    }

    private IEnumerator TransitionToBattle()
    {
        // 启用 Brain，让 Cinemachine 从当前位置混合回战斗视角
        if (_brain != null) _brain.enabled = true;
        yield return null;
    }

    private IEnumerator MoveDrawer(float targetZ)
    {
        float startZ = DrawerTransform.localPosition.z;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / DrawerOpenDuration;
            float z = Mathf.Lerp(startZ, targetZ, Mathf.SmoothStep(0f, 1f, t));
            DrawerTransform.localPosition = new Vector3(
                DrawerTransform.localPosition.x,
                DrawerTransform.localPosition.y,
                z);
            yield return null;
        }
    }

    private IEnumerator MoveCam(Transform target)
    {
        var startPos = _cam.transform.position;
        var startRot = _cam.transform.rotation;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / TransitionDuration;
            float s = Mathf.SmoothStep(0f, 1f, t);
            _cam.transform.position = Vector3.Lerp(startPos, target.position, s);
            _cam.transform.rotation = Quaternion.Slerp(startRot, target.rotation, s);
            yield return null;
        }

        _cam.transform.SetPositionAndRotation(target.position, target.rotation);
    }
}
