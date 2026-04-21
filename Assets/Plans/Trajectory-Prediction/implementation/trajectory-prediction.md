# 实现计划:弹射物理预测指示

**上级文档**:[`../game-design.md`](../game-design.md)

本文档在 GDD 冻结意图后,收敛为可编码结构;落地前应与 `game-design.md` 同步更新「更改日志」。

> **范围说明**:本实现计划聚焦 **仅弹射物理预测指示**,底层采用物理镜像方案(与 GDD §3.1 对齐)。对手 AI / 回合制行动方不在本文范围。

---

## 背景

战斗层已有 `BattleStateMachine`、`IdleState` / `ActionState` / `ResultState`、`BattleContext`(`LaunchDirection`、`LaunchForce`、`ContactPointWorld`)与 `PenEntity.Launch`(`AddForceAtPosition` + 零件倍率 + 恒定阻力)。玩家蓄力阶段目前无轨迹/落点预测反馈。仓库中已存在 `Assets/Scenes/Prediction.unity` 作为该系统的开发/演示场景(未跟踪)。

GDD 已明确:**预测的唯一底层为物理镜像**(独立 `PhysicsScene`),玩家可见的分级为「披露分级」(D0/D1/D2),不再存在 L0/L1 解析近似实现。

---

## 目标

- 实现单例物理镜像世界 `PhysicsMirrorWorld`,托管独立 `PhysicsScene` 与 **双笔完整装配副本**(玩家笔 + 敌方笔均通过 `PenAssembly.InitData + BuildBattleView` 在镜像场景构建,barrel + 所有子零件的 Collider / PhysicsMaterial / 质量聚合 1:1 对齐主场景)+ 台面副本。
- 实现 `TrajectorySimulator`:在镜像中对 `LaunchInput` 调用等价 `Launch`,以固定步长步进 `PhysicsScene.Simulate`,采样位姿序列到 `TrajectorySample`。
- 实现 `TrajectoryDisclosureFilter`:按 `DisclosureLevel`(`Minimal` / `Trend` / `Full`)对同一份 `TrajectorySample` 裁剪为可渲染数据,**不触发重仿真**。
- 实现 `TrajectoryPreviewController` / `TrajectoryPreviewPresenter`:节流、接入 Idle 拖拽、按披露级别渲染、释放即收起。
- 核心逻辑(Simulator / Filter)尽量 **可注入、可单测**(纯 C# + 接口化时间/随机源);镜像管理侧用薄封装便于测试替身。

---

## 非目标

- 不引入对手 AI、回合制行动方、敌方发射逻辑(独立 GDD)。
- **不提供任何解析近似 fallback**;若镜像初始化失败,直接退化到 `Minimal` 级别(等同关闭预测),不得用质点/折线近似「凑」出预测(与 GDD §6 对齐)。
- 首版不要求网络同步、回放确定性。
- 首版不纳入笔-笔碰撞动力学与零件 OnLaunch 持续力(GDD §3.3 非目标)。

---

## 方案概述

1. **镜像世界**:`PhysicsMirrorWorld` 在首次需要时创建独立 `Scene`(`SceneManager.CreateScene` + `CreateSceneParameters(LocalPhysicsMode.Physics3D)`),持有 **双笔完整装配副本**(`AddComponent<Rigidbody>` + `AddComponent<PenAssembly>` → `SetData` + `BuildBattleView`,等价主场景装配)+ 台面副本 + **`extraColliders` 列表里的静态物理元素克隆**(Instantiate 原 GO → 移入镜像 Scene → 禁用所有 MB/Renderer/Rigidbody.useGravity,保留 Collider + PhysicsMaterial);暴露 `SyncFrom(snapshot)` / `EnsureInitialized()` / `Step(dt)`。装配变化(BarrelData 换杆 / PartEntries 换零件)时重建副本。敌方动态属性(mass/COM/damping/useGravity/constraints)每次 SyncFrom 都完整拷贝,避免漂移。
2. **快照同步**:`PenSnapshot` 从主场景 `Rigidbody` / `PenEntity` 抽取 **动态属性**(位姿、mass、COM、阻尼、弹射参数、停机/掉落阈值、`CapsuleHalfHeight` 用于 `ApplyLaunch` 力臂钳制)。**装配结构**(barrel + 零件列表)不走 Snapshot,由 `PhysicsMirrorWorld` 在 `SyncFrom` 时从 `sourcePen.Assembly` / `sourceEnemyPen.Assembly` 直接读取并比对,变化时重建。
3. **仿真器**:`TrajectorySimulator.Simulate(world, input, config) → TrajectorySample`;内部等价 `Launch` 后循环 `PhysicsScene.Simulate(config.FixedStep)`,采样位姿/速度;**停止判据为双笔都满足静止阈值**(或玩家掉落 / MaxSteps / MaxTime),记录 `StopReason`。循环中监听 `TrajectoryCollisionProbe.HasCollided`(镜像玩家笔上挂,`watchedRigidbody = 镜像敌方 rb`),首次发生时记录 `CollisionTime` + 玩家瞬时位姿,供 OnCollide 级别截断。
4. **披露过滤**:`TrajectoryDisclosureFilter.Filter(sample, level, filterConfig) → DisclosedTrajectory`:
   - `Minimal`:空披露;Presenter Hide。
   - `Trend`:方向带 + 玩家 ghost(最终位姿)。
   - `OnCollide`:方向带 + 折线截至 `CollisionTime`(未碰撞则走完全程)+ 玩家 ghost(碰撞瞬间位姿,未碰时退化到最终);**不显示敌方 ghost**(碰撞瞬间敌方尚未动)。
   - `Full`:方向带 + 完整折线 + 玩家 ghost(最终)+ **敌方 ghost**(`sample.FinalEnemyPosition/Rotation`,被撞后的静止姿态)。
5. **表现层**:`TrajectoryPreviewPresenter` 消费 `DisclosedTrajectory` 按级别选择渲染管线;**ghost pen** 在首次 Show 时以 `sourcePenForGhost.gameObject` 为模板 runtime 克隆(移除物理组件与脚本,材质替换为半透明),每次 Show 移动到 `GhostPenPosition/Rotation`。`TrajectoryPreviewController` MonoBehaviour 编排节流、事件订阅、显隐。

详细类型与路径见下文「可编码的代码结构」与「实际代码层」。

---

## 可编码的代码结构

### 命名空间

- 与现有脚本一致:**全局命名空间**(与 `PenEntity`、`BattleStateMachine` 一致)。本轮落地未引入 `Pen.Trajectory` 命名空间;若后续脚本规模扩大再统一。

### 类型职责(拟议)

| 类型 | 职责 |
|------|------|
| `LaunchInput` | 只读结构体:`Direction`, `Force`, `ContactPointWorld`, `PenSnapshot` |
| `PenSnapshot` | 只读快照:位姿、mass、COM、线/角阻尼、`GetLaunchMultiplier()`、`MaxLaunchImpulse` / `SpinResponseFactor` / `CapsuleHalfHeight`、停机/掉落阈值。**不含装配结构** |
| `PhysicsMirrorWorld` | 管理独立 `PhysicsScene`;创建/重建 **双笔完整 `PenAssembly` 装配副本**(`AddComponent<PenAssembly>` → `SetData` + `BuildBattleView`)+ 台面副本;`SyncFrom(PenSnapshot)` 同步玩家动态属性 + 敌方静态位姿;装配差异(BarrelData / PartEntries)检测到即重建 |
| `MirrorSimulationConfig` | `FixedStep`, `MaxSteps`, `MaxTime`, `SampleStride`, `RestFrameCount`, `IncludeEnemyPen` 等步进参数 |
| `TrajectorySample` | 完整采样:点列(位置 + 旋转 + 速度 + 时间)、最终位姿、`StopReason`(`Rested` / `Fallen` / `Timeout` / `Aborted` / `NotStarted`) |
| `TrajectorySimulator` | `Simulate(PhysicsMirrorWorld, LaunchInput, MirrorSimulationConfig) → TrajectorySample`;内置 Sample 缓冲复用 |
| `DisclosureLevel` | 枚举:`Minimal` / `Trend` / `Full` / `OnCollide`(对应 GDD D0 / D1 / D2 / D1.5;整数值固定为 0/1/2/3 避免 Inspector 序列化漂移) |
| `TrajectoryCollisionProbe` | MonoBehaviour;挂在镜像玩家笔 GO 上,`OnCollisionEnter` 遇到 `watchedRigidbody`(镜像敌方 rb)时记 `HasCollided`;`ResetProbe()` 供 Simulator 每次 Simulate 前清状态 |
| `DisclosedTrajectory` | 按级别裁剪后的可渲染数据(方向向量、落点椭圆、折线、可选姿态) |
| `TrajectoryDisclosureFilter` | 纯函数:`Filter(TrajectorySample, DisclosureLevel, FilterConfig, DisclosedTrajectory)` |
| `TrajectoryPreviewPresenter` | MonoBehaviour:`Show(DisclosedTrajectory)` / `Hide()`;按级别启停不同 LineRenderer / 预览 Mesh |
| `TrajectoryPreviewController` | MonoBehaviour:监听 `BattleStateMachine.Ctx`,节流调用 Simulator + Filter,驱动 Presenter,释放即收起;持有 `DisclosureLevel` SerializeField |

### 数据流(简述)

玩家拖拽变化(`IdleState` 下)→ `Controller.Update` 轮询 `battleStateMachine.Ctx` →
节流命中 → `PhysicsMirrorWorld.SyncFrom(PenSnapshot.From(ctx.pen))` →
`TrajectorySimulator.Simulate` → 得 `TrajectorySample`(缓存)→
`TrajectoryDisclosureFilter.Filter(sample, level)` → `TrajectoryPreviewPresenter.Show`。

披露级别切换(未重新拖拽)→ 跳过 Sync/Simulate,仅重新 Filter + Show。

释放 / 离开 Idle / 力度不足 / 镜像未就绪 → `Presenter.Hide`。

### 拟议文件树

```
Assets/Scripts/Trajectory/
  LaunchInput.cs              // LaunchInput / PenSnapshot
  PhysicsMirrorWorld.cs
  MirrorSimulationConfig.cs
  TrajectorySample.cs
  TrajectorySimulator.cs
  DisclosureLevel.cs
  DisclosedTrajectory.cs
  TrajectoryDisclosureFilter.cs
  TrajectoryPreviewController.cs
  TrajectoryPreviewPresenter.cs
```

---

## 实际代码层(CS 脚本)

> **状态**:首版 v1 已落地(2026-04-22)。下方清单以仓库实际 `.cs` 为准。未抽接口 `ITrajectorySimulator` / `ITrajectoryPredictor` —— 首版 YAGNI,后续如需测试替身或多实现再抽。

### 落地文件清单

| 路径 | 类别 | 对外关键 API |
|------|------|-------------|
| `Assets/Scripts/BattleStateMachine.cs` | 修改(现有) | 新增 `public BattleContext Ctx => ctx;` 与 `public PenEntity EnemyPen => enemyPen;`,供预测系统观察蓄力和获取敌方笔引用;其余未变 |
| `Assets/Scripts/Trajectory/LaunchInput.cs` | 新增 | `readonly struct LaunchInput(dir, force, contact, PenSnapshot)`;`readonly struct PenSnapshot`(位姿 / mass / COM / 阻尼 / 弹射参数 / `CapsuleHalfHeight` 力臂钳制 / **`RigidbodyConstraints`(主场景锁 X/Z 旋转须同步到镜像,否则笔翻滚)** / 停机 / 掉落阈值);`PenSnapshot.From(PenEntity)` 单一真源抽取;**装配结构不入快照**,由 Mirror 直接读 `PenAssembly` |
| `Assets/Scripts/Trajectory/DisclosureLevel.cs` | 新增 | `enum Minimal / Trend / Full`(D0/D1/D2) |
| `Assets/Scripts/Trajectory/TrajectorySample.cs` | 新增 | `class TrajectorySample`:位置/旋转/速度/时间点列缓冲 + `InitialPosition` / `InitialVelocityHint` / `FinalPosition` / `FinalRotation` / `Duration` / `StopReason`;`Clear()` 复用。`enum TrajectoryStopReason { NotStarted, Rested, Fallen, Timeout, Aborted }` |
| `Assets/Scripts/Trajectory/DisclosedTrajectory.cs` | 新增 | `class DisclosedTrajectory`;按级别填 `ShowDirectionBand` / `ShowGhostPen`(+ `GhostPenPosition` / `GhostPenRotation`) / `ShowFullPath`;`Reset(level)` 复用 |
| `Assets/Scripts/Trajectory/MirrorSimulationConfig.cs` | 新增(ScriptableObject) | `CreateAssetMenu: GameData/Trajectory/MirrorSimulationConfig`;字段见 Inspector 表 |
| `Assets/Scripts/Trajectory/TrajectoryDisclosureFilter.cs` | 新增(纯函数) | `static Filter(sample, level, in FilterConfig, output)`;嵌套 `[Serializable] struct FilterConfig` + `FilterConfig.Default` |
| `Assets/Scripts/Trajectory/TrajectorySimulator.cs` | 新增(纯 C# 类) | `Simulate(PhysicsMirrorWorld, LaunchInput, MirrorSimulationConfig) → TrajectorySample`;内置缓冲每次复用;`ApplyLaunch` 与 `PenEntity.Launch` 保持 1:1 冲量/施力点语义 |
| `Assets/Scripts/Trajectory/PhysicsMirrorWorld.cs` | 新增(MonoBehaviour) | `EnsureInitialized()` / `SyncFrom(PenSnapshot)` / `Step(float dt)` / `MirrorPen`(玩家副本 Rigidbody)/ `MirrorEnemyPen` / `Physics` / `IsReady` / `IsFailed`;首次失败即永久失败;双笔各自按 `PenAssembly` 装配差异自动重建 |
| `Assets/Scripts/Trajectory/TrajectoryPreviewPresenter.cs` | 新增(MonoBehaviour) | `Show(DisclosedTrajectory)` / `Hide()`;方向带 / **玩家 ghost pen** / **敌方 ghost pen**(Full 级别)/ 完整折线四组渲染源;ghost pen runtime 克隆 `sourcePenForGhost` / `sourceEnemyPenForGhost`(禁用所有组件,材质替换为半透明) |
| `Assets/Scripts/Trajectory/TrajectoryCollisionProbe.cs` | 新增(MonoBehaviour) | 轻量碰撞探针;由 `PhysicsMirrorWorld.AttachPlayerCollisionProbe` 自动挂到镜像玩家笔 |
| `Assets/Scripts/Trajectory/TrajectoryPreviewController.cs` | 新增(MonoBehaviour) | Update 管线;`DisclosureLevel` / `EnablePreview` 运行时可改属性;通过 `_lastRenderedLevel` 比较实现级别切换不重仿真 |
| `Assets/Scripts/Editor/PredictionSceneSetup.cs` | 新增(Editor) | `[InitializeOnLoad]` + `[MenuItem("Tools/Trajectory/Setup Prediction Scene")]`;打开 `Assets/Scenes/Prediction.unity` 或执行菜单时,自动创建 `TrajectoryPreview` 节点树、挂三组件并填全部 `SerializeField` 引用;首次创建 `MirrorSimulationConfig_Default.asset` |

### Inspector 字段清单(已落地)

**`PhysicsMirrorWorld`**(挂场景空物体)

| 字段 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `sourcePen` | PenEntity | null | 主场景玩家笔;镜像通过 `sourcePen.Assembly` 读取 `BarrelData` + `AssembledParts` 在镜像场景重建装配。`PenAssembly.BuildBattleView` 未完成时 `penCollider` / `BarrelData` 为空,`EnsureInitialized` 会失败 |
| `sourceEnemyPen` | PenEntity | null | 主场景敌方笔;同样复用 `PenAssembly` 构建镜像装配;蓄力阶段视为静态(不 `ApplyLaunch`),每次 SyncFrom 完整同步 mass/COM/damping/constraints/位姿 |
| `tableCollider` | Collider | null | 主场景台面 Collider;按其 `bounds` 创建副本 BoxCollider(首版 AABB 近似) |
| `extraColliders` | Collider[] | [] | 额外的静态物理元素列表(桌脚 / 墙壁 / 凸起 / 障碍物);每项所在 GameObject 被 `Instantiate` 到镜像 Scene,保留 Collider/PhysicsMaterial,禁用 MB/Renderer/Rigidbody.useGravity |
| `mirrorSceneName` | string | "TrajectoryMirror" | 独立 Scene 名,仅 Hierarchy / Profiler 识别用 |

**`MirrorSimulationConfig`**(ScriptableObject 资产)

| 字段 | 类型 | 默认 | 范围 | 说明 |
|------|------|------|------|------|
| `FixedStep` | float | 0.02 | >0 | 镜像单步时长(秒);建议与主场景 `Time.fixedDeltaTime` 对齐 |
| `MaxSteps` | int | 600 | [1, 2000] | 步数硬上限 |
| `MaxTime` | float | 6.0 | ≥FixedStep | 时间硬上限(秒);与 `MaxSteps` 取先触发者 |
| `SampleStride` | int | 2 | [1, 20] | 每 N 步采样一次(1 = 每步);越大折线越稀 |
| `RestFrameCount` | int | 3 | [1, 20] | 连续满足停止阈值的帧数;避免单帧噪声误判 `Rested` |
| `IncludeEnemyPen` | bool | false | — | 首版保持 false;true 时镜像需构建敌方笔副本(未实现) |

**`TrajectoryPreviewPresenter`**

| 字段 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `directionBandRenderer` | LineRenderer | null | Trend 级别的初速度方向指示带 |
| `sourcePenForGhost` | PenEntity | null | **玩家** ghost pen 克隆模板;Presenter 首次 Show 时克隆此 `PenEntity.gameObject`(组件全部禁用,材质替换半透明);须指向主场景玩家笔 |
| `sourceEnemyPenForGhost` | PenEntity | null | **敌方** ghost pen 克隆模板(Full 级别用于展示敌方被撞后的最终位姿);须指向主场景敌方笔;留空则 Full 不显示敌方 ghost |
| `ghostMaterial` | Material | null(自动) | 克隆笔使用的半透明材质;为 null 时 Presenter 运行时创建一个 URP Unlit 透明白色(alpha 0.3)作为共享材质 |
| `fullPathRenderer` | LineRenderer | null | Full / OnCollide 级别的完整路径折线 |
| `lineWidth` | float | 0.03 | 两个 LineRenderer 的 `widthMultiplier` |

**`TrajectoryPreviewController`**

| 字段 | 类型 | 默认 | 范围 | 说明 |
|------|------|------|------|------|
| `battleStateMachine` | BattleStateMachine | null | — | 必填;读 `Ctx` 与 `IsIdle` |
| `mirrorWorld` | PhysicsMirrorWorld | null | — | 必填 |
| `simulationConfig` | MirrorSimulationConfig | null | — | 必填;SO 资产 |
| `presenter` | TrajectoryPreviewPresenter | null | — | 必填 |
| `disclosureLevel` | DisclosureLevel | Trend | — | D0 Minimal / D1 Trend / D1.5 OnCollide / D2 Full;运行时可改,不触发重仿真 |
| `filterConfig.TrendDirectionMetersPerSpeed` | float | 0.06 | ≥0 | Trend 方向带长度 = 初速度大小 × 此值(米/(m/s)) |
| `filterConfig.TrendDirectionMaxLength` | float | 0.9 | ≥0 | Trend 方向带硬长度上限(米) |
| `filterConfig.FullDecimation` | int | 1 | ≥1 | Full 级别对 Sample 点列的二次抽稀步长(1 = 不抽稀) |
| `throttleMs` | float | 60 | [0, 500] | 两次仿真最小间隔(毫秒) |
| `dragDeltaThreshold` | float | 0.02 | ≥0 | 触发重仿真的 direction / contact / force 最小变化(节流已过后生效) |
| `minForceToPredict` | float | 0.02 | [0, 1] | 蓄力力度低于此值不预测(Presenter 隐藏) |
| `enablePreview` | bool | true | — | 全局开关 |
| `onlyShowWhileDragging` | bool | true | — | 勾选时只在 `BattleStateMachine.IsDragging`(Idle 子状态 + 玩家按下 / 点击切换拖拽中)时显示预测;默认 true,避免 Idle 初帧因上次 `LaunchForce` 残留瞬显旧预测 |

### 已知落地差异(相对规划)

- **未引入独立 `SceneSnapshot` 结构**:首版台面几何通过 `PhysicsMirrorWorld.tableCollider` SerializeField 引用,按 `bounds` 构造 BoxCollider(AABB 近似)。若后续台面旋转或形状复杂,再抽 `SceneSnapshot`。
- **未抽接口 `ITrajectorySimulator` / `ITrajectoryPredictor`**:当前只有一个实现,直接使用具体类(YAGNI);待第二个实现(Mock / 替换方案)出现时再抽。
- **`MirrorSimulationConfig.IncludeEnemyPen` 字段已无效**:双笔镜像默认都构建,`IncludeEnemyPen` 保留为 Inspector 字段但代码路径忽略(将在后续版本清理)。
- **装配自动重建**:`PhysicsMirrorWorld` 在每次 `SyncFrom` 前比对 `BarrelData` + `AssembledParts`(Count + 逐项 Data/Socket),差异即 `ClearBattleView` → `SetData` → `BuildBattleView` 重建镜像副本;质量/COM 等由 `PenPhysicsAggregator.Recalculate` 同路径重算,无需额外逻辑。
- **零件 VisualPrefab 脚本副作用**:镜像中零件实例会触发 `Awake`,感觉反馈类(`PenCollisionFeedback`)与 Camera.main 等无副作用;若某零件引入持续力脚本(RocketEffect 等),镜像中亦会运行,**纳入预测** —— 与 GDD §3.3 一致。

---

## 变更清单(按文件路径)

- `Assets/Scripts/BattleStateMachine.cs`:新增 `public BattleContext Ctx => ctx;` / `public PenEntity EnemyPen => enemyPen;` / `public bool IsDragging`(委托给 `IdleState.IsDragging`)三个 getter + `TryEnsureEnemyAssembly`(Update 首次兜底用玩家默认装配补建敌方 `PenAssembly`)。
- `Assets/Scripts/BattleState/IdleState.cs`:新增 `public bool IsDragging => isDragging;` getter(其余未变)。
- `Assets/Scripts/Trajectory/**`(新建):10 个 `.cs`(LaunchInput / DisclosureLevel / TrajectorySample / DisclosedTrajectory / MirrorSimulationConfig / TrajectoryDisclosureFilter / TrajectorySimulator / PhysicsMirrorWorld / TrajectoryPreviewPresenter / TrajectoryPreviewController)。
- `Assets/Scripts/Editor/PredictionSceneSetup.cs`(新建):Editor 工具脚本,自动/手动配置 Prediction 场景。
- `Assets/Scenes/Prediction.unity`:预测系统开发/演示场景(已存在;读取或修改 `.unity` 须 **用户已授权或已点名路径**)。
- `Assets/Prefabs/**`:预览对象 Prefab(方向带、落点椭圆、Full 折线)(**需改 Prefab 时在本清单勾选并在 Checklist 中注明已获用户授权或已点名路径**)。
- `Assets/ScriptableObjects/**`(待创建):`MirrorSimulationConfig` 资产实例。

---

## 风险与回滚

- **风险**:镜像场景物理参数与主场景漂移 → 单一真源(`PenSnapshot.From(PenEntity)` 运行时读取);装配变更后 `NeedsRebuildPen` 按 Capsule 几何自动重建。
- **风险**:`Rigidbody.position` setter 不自动同步 transform,首帧 Simulate 碰撞检测可能用旧位置 → 镜像笔疑似穿台面 / 无摩擦飞远 → `SyncFrom` 末尾显式调用 `Physics.SyncTransforms()` 修正;镜像笔 `collisionDetectionMode = ContinuousDynamic` 防高速穿透兜底。
- **风险**:`Rigidbody.constraints`(锁 X/Z 旋转等)属于动态物理属性,镜像不同步会导致镜像笔自由翻滚 / 预测姿态错误 → `PenSnapshot` 带 `Constraints`,`SyncFrom.ApplyPlayerDynamics` + 敌方位姿同步都拷贝;装配流程不改 constraints,但保留此钩以防未来变动。
- **风险**:Ghost pen 克隆时删除组件会因 `[RequireComponent]` 依赖链(`PenCOMVisualizer → Rigidbody/PenAssembly`、`PenCollisionFeedback → PenEntity`)报错拒绝 → **改为"不删除,全部禁用"**:`MonoBehaviour.enabled = false`;`Rigidbody` 设 `isKinematic = true` + `useGravity = false` + `FreezeAll` + `detectCollisions = false`;`Collider.enabled = false`。Ghost 只需外观 + 可摆放 transform,不需要实体物理。
- **风险**:Unity 主相机渲染 **所有已加载 Scene 的 Renderer**(镜像 Scene 也不例外)→ 镜像笔 barrel/零件的 MeshRenderer 会与主场景笔叠加显示,观感上像"mirror 没隐藏" → `BuildAssembledMirror` / `RebuildAssembly` / `BuildExtraMirror` 末尾遍历 `GetComponentsInChildren<Renderer>` 全部 `enabled = false`。镜像只保留物理,无视觉。
- **风险**:镜像步进成本过高 → 节流窗 + 步数/时间上限 + 停止速度阈值;必要时分帧(`IEnumerator`)或仅预测到关键事件(掉落 / 碰撞边界)。
- **风险**:镜像初始化失败(平台/API 限制)→ `PhysicsMirrorWorld.EnsureInitialized` 返回 false,Controller 退化到 `Minimal`(Hide),日志告警;**不退化到解析近似**。
- **风险**:披露级别引发「观感作弊」讨论 → 默认 `Trend`;`Full` 优先作为训练/辅助开关;文档显式声明。
- **风险**:`ApplyLaunch` 与 `PenEntity.Launch` 双份实现 → 当前靠注释相互提示;若 `PenEntity.Launch` 修改,需要同步 `TrajectorySimulator.ApplyLaunch`。长期若变动频繁,考虑抽静态工具方法。
- **回滚**:`TrajectoryPreviewController.enablePreview` SerializeField 默认可关闭;`PhysicsMirrorWorld` 与 `Simulator` 可脱离 Controller 独立在 PlayMode 测试场景验证,便于关闭表现层排查根因。

---

## 测试计划

1. **单元测试**(若引入 Test Runner):
   - `TrajectoryDisclosureFilter` 三级裁剪结果:`Minimal` 空轨迹、`Trend` 无折线但含方向+椭圆、`Full` 含完整点列。
   - `TrajectorySimulator` 在已知 `LaunchInput`(零力度、水平冲量、零阻尼简化)下 `TrajectorySample` 的预期性质(长度、末段速度趋势、`StopReason`)。
   - 边界:`Force=0` / `Direction=Vector3.zero` 应产出仅初始点或空样本而不抛异常;`PenSnapshot` 无效(`IsValid=false`)时行为明确。
2. **一致性测试**(PlayMode):镜像与主场景对同一 `LaunchInput` 运行 N 步,对比位姿误差 < ε(N、ε 在 `MirrorSimulationConfig` 或测试常量中定义);装配变更后首次 Predict 能反映新参数(`NeedsRebuildPen` 重建)。
3. **PlayMode 集成**:蓄力全行程预测显示符合当前级别;级别切换不重仿真(通过性能探针或调用计数验证);释放后立即收起;镜像不对主场景物体产生副作用(层/碰撞隔离)。
4. **目测**:像素风格下各级别表现清晰;`Trend` 的落点椭圆不误导;`Full` 折线无闪烁撕裂。

---

## Checklist

- [x] `PhysicsMirrorWorld` 独立 `PhysicsScene` 创建/销毁正确,不干扰主场景(代码通过 `SceneManager.CreateScene` + `LocalPhysicsMode.Physics3D`;`OnDestroy` 卸载)
- [x] **双笔完整装配**到镜像(`AddComponent<PenAssembly>` + `SetData` + `BuildBattleView`);装配差异(BarrelData / PartEntries)自动触发重建;敌方笔按静态位姿每次 SyncFrom 同步
- [x] **敌方默认装配补建**:`BattleStateMachine.TryEnsureEnemyAssembly` 在 Update 首次用玩家 loadout 补建敌方 `PenAssembly`,使敌方笔在物理上参与镜像碰撞
- [x] **OnCollide 披露级别**:Filter 按 `CollisionTime` 截断折线 + 玩家 ghost 置于碰撞位姿;`Full` 改为 **双笔静止停止判据** + 敌方 ghost(`FinalEnemyPosition/Rotation`)
- [x] `PenSnapshot` 能从运行时 `PenEntity` / `PenAssembly` 读取并驱动镜像同步(`PenSnapshot.From` + `SyncFrom`);`SceneSnapshot` 未落地,见「已知落地差异」
- [x] `TrajectorySimulator` 在镜像中调用等价 `Launch` 并产出完整 `TrajectorySample`(含 `StopReason`)
- [x] `DisclosureLevel` 三档(`Minimal` / `Trend` / `Full`)+ `TrajectoryDisclosureFilter` 裁剪到 `DisclosedTrajectory`
- [x] 级别切换复用 `TrajectorySample`,不触发重仿真(Controller 通过 `_lastRenderedLevel` 比较;仅 level 变化时只重走 Filter + Show)
- [x] `TrajectoryPreviewController` / `TrajectoryPreviewPresenter` 在蓄力阶段按级别渲染,释放/离开 Idle/力度不足即收起
- [x] 镜像初始化失败时退化到 `Minimal`,不使用任何解析近似 fallback
- [x] 每个新增/修改 `.cs` 已在本文「实际代码层」补全契约(路径、字段清单、对外 API)
- [x] Unity 集成:`Assets/Scripts/Editor/PredictionSceneSetup.cs`(`[InitializeOnLoad]` + `Tools/Trajectory/Setup Prediction Scene` MenuItem)自动挂载 TrajectoryPreview 根、子对象与组件引用,并首次创建 `MirrorSimulationConfig_Default.asset`;打开 `Assets/Scenes/Prediction.unity` 自动触发(用户已授权场景编辑,2026-04-22)。实机验证由用户在 PlayMode 完成
- [ ] 测试计划对应项(尤其一致性误差验证)已执行或记录豁免原因
- [x] `../game-design.md` 参数未变动,不需同步;[`../editor-setup.md`](../editor-setup.md) 已按本文 Inspector 字段清单创建并对齐(含影响/推荐值列与排错表)
- [x] 本文件「更改日志」已更新

---

## 更改日志

| 日期 | 摘要 |
|------|------|
| 2026-04-20 | 初版:与 GDD 配套的实现计划骨架(含对手 AI + 预测);待脚本落地后补全 CS 契约与 editor-setup |
| 2026-04-21 | 范围收窄:移除对手 AI / 回合制行动方 / `EnemyLaunchPlanner` 相关章节与文件条目,计划仅聚焦弹射物理预测指示;补充 `Assets/Scenes/Prediction.unity` 作为落地场景,细化 Inspector 字段范围 |
| 2026-04-21 | 上级目录重命名为 `Trajectory-Prediction/`,本文件重命名为 `trajectory-prediction.md` |
| 2026-04-21 | 方案重构:统一底层为物理镜像(`PhysicsScene`),引入 `PhysicsMirrorWorld` / `TrajectorySimulator` / `DisclosureLevel` / `TrajectoryDisclosureFilter`;废止 L0/L1 解析近似实现,分级改为披露裁剪;文件清单、数据流与 Inspector 字段同步更新 |
| 2026-04-22 | 首版代码实现落地:`Assets/Scripts/Trajectory/` 下新增 10 个 `.cs` + `BattleStateMachine` 暴露 `Ctx` getter;三层管线(MirrorWorld / Simulator / Filter+Presenter)跑通;Inspector 字段清单与对外 API 按实际代码同步;记录落地差异(SceneSnapshot / ITrajectorySimulator / 敌方笔未实现) |
| 2026-04-22 | 新增兄弟配置指南 [`../editor-setup.md`](../editor-setup.md)(按 CLAUDE.md 固定章节顺序),参数与本文「实际代码层」清单逐项对齐;Checklist 对应项更新为已创建 |
| 2026-04-22 | 新增 `Assets/Scripts/Editor/PredictionSceneSetup.cs`:`[InitializeOnLoad]` 与 `Tools/Trajectory/Setup Prediction Scene` MenuItem,打开 Prediction.unity 时自动创建 `TrajectoryPreview` 节点树、挂三组件并填全部 SerializeField 引用,首次同步创建 `MirrorSimulationConfig_Default.asset`;editor-setup.md「一键配置」小节同步 |
| 2026-04-22 | 方案加强:`PhysicsMirrorWorld` 重写为 **双笔完整 PenAssembly 装配**(主场景 `PenAssembly.BuildBattleView` 同路径);`PenSnapshot` 去除 Capsule 几何(radius / direction / material / center),仅保留 `CapsuleHalfHeight` 用于 ApplyLaunch 力臂钳制;新增 SerializeField `sourceEnemyPen`;`BattleStateMachine` 增 `EnemyPen` getter;`PredictionSceneSetup` 自动填 `sourceEnemyPen`;装配变更按 `BarrelData + AssembledParts` diff 重建 |
| 2026-04-22 | Bug 修复 #1(预测距离偏远):`PhysicsMirrorWorld.SyncFrom` 末尾补 `Physics.SyncTransforms()`,镜像笔 Rigidbody `collisionDetectionMode = ContinuousDynamic`;根因是 `rb.position` setter 不自动同步 transform → 首帧碰撞检测用旧位置 → 疑似穿透台面 → 无摩擦飞远 |
| 2026-04-22 | 视觉重构 #2:废止落点椭圆/色带,改为 **半透明 ghost pen**(玩家笔视觉克隆,runtime 在 Presenter 内构建);`DisclosedTrajectory` 删 `ShowLandingEllipse/LandingCenter/LandingRadius`,加 `ShowGhostPen/GhostPenPosition/GhostPenRotation`;`FilterConfig` 删 `TrendLandingBaseRadius/TrendLandingRadiusPerSecond`;Presenter 加 `sourcePenForGhost / ghostMaterial` SerializeField,移除 `landingEllipseRoot/landingEllipseHeight`;`PredictionSceneSetup` 不再创建 `LandingEllipse` 子物体,自动赋 `sourcePenForGhost = sourcePen` |
| 2026-04-22 | Bug 修复:(a)`PenSnapshot` 新增 `RigidbodyConstraints Constraints` 字段,`From(pen)` 抓 `rb.constraints`,`PhysicsMirrorWorld.ApplyPlayerDynamics` + 敌方位姿同步都应用 —— 解决镜像笔 X/Z 旋转未锁导致翻滚;(b)`TrajectoryPreviewPresenter.EnsureGhostPen` 改 `DestroyImmediate`,顺序改为 MB → Rigidbody → Collider,绕开 `[RequireComponent]` 依赖链报错 |
| 2026-04-22 | Bug 修复再进:`DestroyImmediate` 按 `GetComponentsInChildren<MonoBehaviour>` 顺序遍历,顺序不保证拓扑有序,依赖链仍可能触发 "Can't remove X because Y depends on it"。改策略为 **不删除、全部禁用**:MB `enabled=false` + Rigidbody `isKinematic=true`/`useGravity=false`/`FreezeAll`/`detectCollisions=false` + Collider `enabled=false`;ghost 只需外观 + 可摆放 transform,物理实体不必真删 |
| 2026-04-22 | #3 敌方默认装配:`BattleStateMachine.TryEnsureEnemyAssembly` 首次 Update 检查 `enemyPen.Assembly.BarrelData == null` 时用玩家 `PenAssembly` 的 `BarrelData + AssembledParts` 调 `SetData + BuildBattleView`,使敌方笔具备与玩家同样的复合物理 |
| 2026-04-22 | #4 OnCollide 披露级别 + Full 扩展:新增 `DisclosureLevel.OnCollide = 3`;新增 `TrajectoryCollisionProbe`(镜像玩家笔上监听与镜像敌方笔的首次碰撞);`TrajectorySample` 加 `CollisionOccurred/Time/CollisionPlayerPosition/Rotation` 与 `HasEnemy/FinalEnemyPosition/Rotation`;`Simulator` 停止判据改为**双笔都静止**(Full 语义);`Filter` 新增 OnCollide 分支(折线截至碰撞、玩家 ghost 在碰撞位姿);`Full` 分支新增敌方 ghost(`FinalEnemyPosition/Rotation`);`Presenter` 加 `sourceEnemyPenForGhost` SerializeField 与运行时敌方 ghost 克隆;`PhysicsMirrorWorld.AttachPlayerCollisionProbe`;`PredictionSceneSetup` 自动赋 `sourceEnemyPenForGhost = sm.EnemyPen` |
| 2026-04-22 | 精度增强 + 交互开关:`PhysicsMirrorWorld` 新增 `extraColliders` SerializeField(`Collider[]`)——将每项所在 GameObject Instantiate 到镜像 Scene,禁用 MB/Renderer/Rigidbody.useGravity,保留 Collider/PhysicsMaterial;敌方 SyncFrom 补齐 mass/COM/damping/useGravity 拷贝(之前只拷位姿与 constraints,易漂移);`IdleState` 暴露 `IsDragging`;`BattleStateMachine` 暴露 `IsDragging`;`TrajectoryPreviewController` 新增 `onlyShowWhileDragging`(默认 true)SerializeField,`ShouldPredictThisFrame` 在开启时要求 `battleStateMachine.IsDragging` 为 true |
| 2026-04-22 | Bug 修复:`PhysicsMirrorWorld.BuildAssembledMirror` / `RebuildAssembly` 末尾 `DisableAllRenderers`,防镜像笔 barrel/零件的 MeshRenderer 被主相机渲染出来(观感"mirror 没隐藏")。未碰撞情况下预测较准确,碰撞后预测仍有偏差(用户确认暂时搁置) |
