# 配置指南:弹射物理预测指示

**上级文档**:[`./game-design.md`](./game-design.md)
**实现计划**:[`./implementation/trajectory-prediction.md`](./implementation/trajectory-prediction.md)

本指南描述如何在 Unity Editor 中挂载、配置、验证 **弹射物理预测指示** 系统。所有参数以仓库实际 `.cs` 的 `[SerializeField]` 为准,与实现计划「实际代码层」字段清单逐项对齐。若两者冲突,**先更新实现计划,再更新本指南**(见 CLAUDE.md 硬规则 1)。

---

## 适用范围

- 覆盖 `Assets/Scripts/Trajectory/` 下新增脚本 + `BattleStateMachine.Ctx` getter 的编辑器端配置。
- **对象**:首次在场景挂载预测系统、调整披露级别与视觉阈值、排查"预测不显示 / 显示错位 / 卡顿"等问题。
- **不覆盖**:对手 AI、回合制、已归档的 L0/L1 解析近似方案(均不在本 GDD 范围)。

---

## 配置入口

| 类型 | 位置 | 说明 |
|------|------|------|
| 开发/演示场景 | `Assets/Scenes/Prediction.unity` | 首版落地场景(未跟踪) |
| 核心脚本 | `Assets/Scripts/Trajectory/` | 10 个 `.cs` 见实现计划「落地文件清单」 |
| 可配置资产 | `Assets/ScriptableObjects/`(推荐路径) | `MirrorSimulationConfig` SO(Create 菜单:`GameData/Trajectory/MirrorSimulationConfig`) |
| 预览对象根 | 场景内推荐节点 `TrajectoryPreview` | 挂 `PhysicsMirrorWorld` + `TrajectoryPreviewPresenter` + `TrajectoryPreviewController`(可同 GO 亦可拆子节点) |

### 相关脚本(仓库路径)

本系统涉及的全部 `.cs`(与实现计划「实际代码层」一致;完整 API / SerializeField 字段契约见该文档):

- `Assets/Scripts/BattleStateMachine.cs` —— 已修改:暴露 `Ctx` / `EnemyPen` / `IsDragging` getter + `TryEnsureEnemyAssembly` Update 兜底
- `Assets/Scripts/BattleState/IdleState.cs` —— 已修改:暴露 `public bool IsDragging => isDragging;` getter,供 Controller `onlyShowWhileDragging` 判定
- `Assets/Scripts/Trajectory/LaunchInput.cs` —— `LaunchInput` / `PenSnapshot` 只读结构体
- `Assets/Scripts/Trajectory/DisclosureLevel.cs` —— 披露级别枚举(Minimal/Trend/Full)
- `Assets/Scripts/Trajectory/TrajectorySample.cs` —— 完整采样容器 + `TrajectoryStopReason`
- `Assets/Scripts/Trajectory/DisclosedTrajectory.cs` —— 按级别裁剪后的可渲染数据
- `Assets/Scripts/Trajectory/MirrorSimulationConfig.cs` —— ScriptableObject,步进参数
- `Assets/Scripts/Trajectory/TrajectoryDisclosureFilter.cs` —— 纯函数过滤器 + `FilterConfig`
- `Assets/Scripts/Trajectory/TrajectorySimulator.cs` —— 镜像步进与采样;与 `PenEntity.Launch` 保持 1:1 冲量语义
- `Assets/Scripts/Trajectory/PhysicsMirrorWorld.cs` —— MonoBehaviour,独立 PhysicsScene 与副本管理
- `Assets/Scripts/Trajectory/TrajectoryPreviewPresenter.cs` —— MonoBehaviour,按级别渲染
- `Assets/Scripts/Trajectory/TrajectoryPreviewController.cs` —— MonoBehaviour,Update 管线编排
- `Assets/Scripts/Trajectory/TrajectoryCollisionProbe.cs` —— MonoBehaviour,由 `PhysicsMirrorWorld` 自动挂到镜像玩家笔上,监听与镜像敌方笔的首次碰撞(供 OnCollide 级别使用)

本指南的「参数说明」只逐项罗列 **带 SerializeField / Inspector 可见字段** 的脚本(`PhysicsMirrorWorld` / `MirrorSimulationConfig` / `TrajectoryPreviewPresenter` / `TrajectoryPreviewController`);其余数据类 / 纯函数 / 枚举不在 Inspector 可配。

---

## 依赖与前置条件

必须满足以下任一项才能成功初始化,否则 `PhysicsMirrorWorld.EnsureInitialized` 失败,系统退化为 `Minimal`(预测不显示):

1. **场景中已有可用的战斗主体**:
   - `BattleStateMachine` 组件(已有)与其 `pen: PenEntity` 引用正确。
   - `PenAssembly.BuildBattleView()` 已跑过,`PenEntity.penCollider`(即 `BarrelCollider`)非空。通常在 `GameManager.ApplyLoadoutOnNewSave` 流程或战斗进入时自动发生。
2. **台面 Collider**(主场景内)已经存在,且 `bounds` 能正确覆盖实际玩家可停留区域(首版按 AABB 近似构建副本)。
3. **Unity 版本**支持 `SceneManager.CreateScene(..., CreateSceneParameters(LocalPhysicsMode.Physics3D))` 与 `PhysicsScene.Simulate`(Unity 2018.3+;本项目为 Unity 6,符合)。
4. **`DisclosureLevel` 默认 `Trend` 可玩;切 `Full` 前确认 `fullPathRenderer` 已赋;切 `Minimal` 不需要任何视觉资源**。

---

## 配置步骤(可复现)

首选走 **一键配置**(`Assets/Scripts/Editor/PredictionSceneSetup.cs` 自动化);仅当需要手工调整或自动查找失败时,再走手动配置。

### 一键配置(推荐)

由 [`Assets/Scripts/Editor/PredictionSceneSetup.cs`](../../Scripts/Editor/PredictionSceneSetup.cs) 提供,基于 `[InitializeOnLoad]` + `EditorSceneManager.sceneOpened`:

1. 打开 `Assets/Scenes/Prediction.unity` 作为激活场景。
2. 若场景中尚无 `TrajectoryPreview` 根节点,Editor 会自动:
   - 创建根 GameObject `TrajectoryPreview` + 子节点 `DirectionBand`(LineRenderer) / `LandingEllipse`(Cylinder + Y scale 0.01) / `FullPath`(LineRenderer)。
   - 挂 `PhysicsMirrorWorld` / `TrajectoryPreviewPresenter` / `TrajectoryPreviewController` 三个组件。
   - 若 `Assets/ScriptableObjects/Trajectory/MirrorSimulationConfig_Default.asset` 不存在,首次创建。
   - 按下文「自动查找策略」填所有 `SerializeField` 引用。
3. 场景被 `MarkSceneDirty`,`Ctrl+S` 保存。
4. 需要重建(例如误操作想回初态)走菜单 `Tools → Trajectory → Setup Prediction Scene`(强制重建,不会删已保存的 SO)。

**自动查找策略**
- `sourcePen` ← `BattleStateMachine.Pen`(场景 `BattleManager` 上引用的玩家 PenEntity)。
- `sourceEnemyPen` ← `BattleStateMachine.EnemyPen`(同上)。若场景无敌方笔,镜像仅构建玩家副本。
- `tableCollider` ← 场景内**首个**名字含 "Table"(大小写不敏感)且无 `Rigidbody`、且不在 PenEntity 层级下的 Collider;若找不到,回退为**最大水平面积的 BoxCollider**(同样排除 Rigidbody 与 PenEntity 层级)。若两条策略都失败,组件会挂好但 `tableCollider` 字段留空,Inspector 提示手工填写,并在 Console 输出警告。

### 手动配置(备选)

若一键路径失效(例如非 Prediction 场景启用预测,或 `tableCollider` 自动查找不合预期),按下列步骤手动拉通:

### 1. 创建 `MirrorSimulationConfig` SO

1. 在 Project 面板右键目标文件夹(推荐 `Assets/ScriptableObjects/Trajectory/`)。
2. `Create` → `GameData` → `Trajectory` → `MirrorSimulationConfig`,命名 `MirrorSimulationConfig_Default`。
3. 保持默认值即可跑通;后续可按「参数说明」调优。

### 2. 建立预览节点层次

在 Hierarchy 根部创建:

```
TrajectoryPreview         (空 GameObject)
├─ DirectionBand          (LineRenderer,2 顶点,默认隐藏)
└─ FullPath               (LineRenderer,空,默认隐藏)
```

命名可按需调整;后续脚本引用依赖 **SerializeField 连接**,不依赖名字。

**Ghost pen** 不在此处预建。Presenter 在首次需要显示时 runtime 克隆 `sourcePenForGhost.gameObject`(移除物理组件与脚本,材质替换为半透明共享材质),挂到 Presenter 下。

### 3. 挂 `PhysicsMirrorWorld`

在 `TrajectoryPreview` 根节点 `Add Component` → `PhysicsMirrorWorld`:

- `sourcePen` ← 主场景玩家笔(通常是 `BattleStateMachine.pen` 指向的那个 `PenEntity`)。
- `sourceEnemyPen` ← 主场景敌方笔(`BattleStateMachine.enemyPen`);留空则镜像无敌方刚体。
- `tableCollider` ← 战斗台面 `Collider`(Table Prefab 的根 Collider)。
- `extraColliders` ← 额外静态物理元素列表(桌脚 / 墙壁 / 凸起 / 障碍物等)。所在 GameObject 会被克隆到镜像 Scene,保留 Collider/PhysicsMaterial,禁用 MB/Renderer。
- `mirrorSceneName` 可保留 `"TrajectoryMirror"`。

### 4. 挂 `TrajectoryPreviewPresenter`

仍在 `TrajectoryPreview` 根节点 `Add Component` → `TrajectoryPreviewPresenter`:

- `directionBandRenderer` ← 子物体 `DirectionBand` 的 `LineRenderer`。
- `sourcePenForGhost` ← 主场景玩家笔 `PenEntity`(玩家 ghost 克隆模板)。
- `sourceEnemyPenForGhost` ← 主场景敌方笔 `PenEntity`(敌方 ghost 克隆模板;Full 级别用于展示被撞后最终位姿)。留空则 Full 级别不显示敌方 ghost。
- `ghostMaterial`(可选)← 自定义半透明材质;留空则 Presenter 运行时创建 URP Unlit 透明白色(alpha 0.3)作为共享。
- `fullPathRenderer` ← 子物体 `FullPath` 的 `LineRenderer`。
- `lineWidth` 可先保留默认。

两个 LineRenderer 需手动配材质(URP:`Universal Render Pipeline/Unlit` 或 `Lit`);默认 Start/End Color 可设为半透明方便肉眼区分。

### 5. 挂 `TrajectoryPreviewController`

同节点 `Add Component` → `TrajectoryPreviewController`:

- `battleStateMachine` ← 场景中 `BattleStateMachine` 所在 GO。
- `mirrorWorld` ← 本节点(或另一节点上挂的 `PhysicsMirrorWorld`)。
- `simulationConfig` ← 步骤 1 创建的 SO。
- `presenter` ← 本节点的 `TrajectoryPreviewPresenter`。
- `disclosureLevel` 保留 `Trend`(D1,默认);首次调试可切 `Full` 看完整折线确认物理一致。
- 其他节流/阈值参数按「参数说明」调。

### 6. 运行验证

1. 进入 PlayMode。
2. 进入战斗(`IdleState`);按住鼠标左键拖拽玩家笔。
3. 预期:`Trend` 级下见方向带 + 落点扁圆;`Full` 级下见完整折线 + 落点。
4. 松开鼠标 / 右键取消 / 力度 < `minForceToPredict` → 预览立即消失。
5. Console 无 `[PhysicsMirrorWorld] 初始化失败` 错误。

---

## 参数说明(所有可见参数)

本节与实现计划「实际代码层」字段清单逐项对齐;表中增列「对玩法/性能影响」与「推荐值」。

### `PhysicsMirrorWorld`(MonoBehaviour)

| 参数 | 类型 | 默认 | 范围/单位 | 影响 | 推荐值 |
|------|------|------|----------|------|--------|
| `sourcePen` | PenEntity | null | — | 必填。镜像通过 `sourcePen.Assembly` 读 `BarrelData` + `AssembledParts` 在镜像场景重建完整装配(`PenAssembly.SetData + BuildBattleView`)。未赋或对应 `PenAssembly.BuildBattleView` 未完成,`EnsureInitialized` 失败,预测降级为 Minimal | 指向 `BattleStateMachine.pen` 的 PenEntity |
| `sourceEnemyPen` | PenEntity | null | — | 可选。若赋值,镜像会构建敌方笔完整装配副本,蓄力阶段视为静态位姿(每次 SyncFrom 同步位置);未就绪时会在下次 SyncFrom 补建。留空则镜像内无敌方刚体,玩家笔预测路径不会与敌方发生碰撞 | 指向 `BattleStateMachine.enemyPen` 的 PenEntity |
| `tableCollider` | Collider | null | — | 必填。按其世界 `bounds` 生成 BoxCollider;非轴对齐台面会有 AABB 近似误差 | 战斗台面根 Collider |
| `extraColliders` | Collider[] | [] | — | 额外静态物理元素;每项 GameObject 被 `Instantiate` 到镜像 Scene,保留 Collider/PhysicsMaterial 并禁用 MB/Renderer/Rigidbody.useGravity。**世界位姿保留,lossyScale 直接拷到 localScale**(嵌套父级非单位缩放时可能有小偏差) | 桌脚、台面边缘凸起、墙壁等能影响笔运动或碰撞预测的静态物体 |
| `mirrorSceneName` | string | "TrajectoryMirror" | — | 仅 Hierarchy/Profiler 识别用,逻辑无影响 | 保留默认 |

### `MirrorSimulationConfig`(ScriptableObject)

| 参数 | 类型 | 默认 | 范围/单位 | 影响 | 推荐值 |
|------|------|------|----------|------|--------|
| `FixedStep` | float | 0.02 | >0,秒 | 小 → 更精确但更慢;大 → 与主场景差异增大 | 与 `Time.fixedDeltaTime` 对齐(0.02) |
| `MaxSteps` | int | 600 | [1, 2000] | 步数硬上限;上限低时远距离预测可能被截成 Timeout | 战斗台面最长停止距离 / `FixedStep`,通常 300–800 |
| `MaxTime` | float | 6.0 | 秒,≥FixedStep | 时间硬上限;与 MaxSteps 取先触发者 | 3–8 秒,按笔停止时长估算 |
| `SampleStride` | int | 2 | [1, 20] | 1 = 每步采样(Full 折线最平滑但数据多);大值减数据量但 Full 折线变稀 | Full 用 1–2;只跑 Trend 可以 3–5 |
| `RestFrameCount` | int | 3 | [1, 20] | 连续几帧满足速度阈值才判 Rested;过小可能单帧噪声误判 | 3–5 |
| `IncludeEnemyPen` | bool | false | — | **已无效字段**:双笔是否构建由 `PhysicsMirrorWorld.sourceEnemyPen` 决定,本字段目前不生效,后续版本清理 | — |

### `TrajectoryPreviewPresenter`(MonoBehaviour)

| 参数 | 类型 | 默认 | 范围/单位 | 影响 | 推荐值 |
|------|------|------|----------|------|--------|
| `directionBandRenderer` | LineRenderer | null | — | Trend 下的方向带;null 则 Trend 无方向提示 | 必填(Trend 使用时) |
| `sourcePenForGhost` | PenEntity | null | — | **玩家** ghost 的克隆模板。首次 Show 时 Presenter 克隆此 `PenEntity.gameObject`,所有组件禁用,材质替换半透明;null 则 Trend/OnCollide/Full 都不显示玩家 ghost | 必填;指向主场景玩家笔 |
| `sourceEnemyPenForGhost` | PenEntity | null | — | **敌方** ghost 的克隆模板,Full 级别下展示被撞后的敌方最终位姿;null 则 Full 不显示敌方 ghost(玩家侧表现不受影响) | 指向主场景敌方笔(`BattleStateMachine.enemyPen`) |
| `ghostMaterial` | Material | null(自动) | — | 玩家/敌方 ghost 共用的半透明材质;留空则运行时创建 URP Unlit 透明白色(alpha 0.3) | 可选;需要美术定制时手工赋资产 |
| `fullPathRenderer` | LineRenderer | null | — | Full 的完整折线;null 则 Full 无折线 | 必填(Full 使用时) |
| `lineWidth` | float | 0.03 | >0,米 | 两个 LineRenderer 的 widthMultiplier;像素风下过粗难读 | 0.02–0.05 |

### `TrajectoryPreviewController`(MonoBehaviour)

| 参数 | 类型 | 默认 | 范围/单位 | 影响 | 推荐值 |
|------|------|------|----------|------|--------|
| `battleStateMachine` | BattleStateMachine | null | — | 必填;读取 `Ctx` 与 `IsIdle` | 指向场景 `BattleStateMachine` |
| `mirrorWorld` | PhysicsMirrorWorld | null | — | 必填 | 同节点或另一节点上的 `PhysicsMirrorWorld` |
| `simulationConfig` | MirrorSimulationConfig | null | — | 必填 | 步骤 1 创建的 SO |
| `presenter` | TrajectoryPreviewPresenter | null | — | 必填 | 同节点的 `TrajectoryPreviewPresenter` |
| `disclosureLevel` | enum | Trend | Minimal/Trend/OnCollide/Full | 玩家感知的预测丰度;不触发重仿真。`OnCollide` 披露到玩家与敌方首次碰撞为止(包含方向带 + 折线 + 玩家 ghost);`Full` 披露到 **所有笔都静止**(含敌方 ghost) | Trend(默认);OnCollide 适合规划攻击时机;Full 做训练/辅助开关 |
| `filterConfig.TrendDirectionMetersPerSpeed` | float | 0.06 | ≥0,米/(m/s) | 方向带长度对速度的响应;越大越长 | 0.04–0.1 |
| `filterConfig.TrendDirectionMaxLength` | float | 0.9 | ≥0,米 | 方向带硬长度上限(防高速穿屏) | 0.6–1.2,视台面尺寸 |
| `filterConfig.FullDecimation` | int | 1 | ≥1 | Full 级对 Sample 点列的二次抽稀;>1 时稀疏但末点兜底仍在 | 1(配合 SampleStride 调 Sample 侧) |
| `throttleMs` | float | 60 | [0, 500],毫秒 | 两次仿真最小间隔;越大越省 CPU 但拖动响应越滞后 | 40–80 |
| `dragDeltaThreshold` | float | 0.02 | ≥0,米/单位 | 节流过后须 direction/contact/force 任一变化超阈值才重仿真;太小会每帧重算 | 0.01–0.05 |
| `minForceToPredict` | float | 0.02 | [0, 1] | 低于此值视为未发力,隐藏 UI | 0.01–0.05 |
| `enablePreview` | bool | true | — | 全局开关;false 时持续 Hide | true |
| `onlyShowWhileDragging` | bool | true | — | 勾选时预测仅在玩家正在拖拽蓄力(`BattleStateMachine.IsDragging == true`)时显示;松手/未开始拖拽即隐藏。避免 Idle 初帧 `LaunchForce` 残留导致瞬显旧预测 | true(推荐);关闭则符合"力度 > 阈值就显示"的旧行为 |

### 已知偏差(与实机对比)

- 镜像台面为 AABB 近似,若主场景 Table 有旋转或复杂轮廓,边缘掉落概率会略偏离实机。
- 镜像双笔采用 `PenAssembly.BuildBattleView` 等价完整装配(barrel + 所有零件各自的 Collider / PhysicsMaterial / 质量聚合),几何层面与实机 1:1;仍可能存在的差异来自镜像中零件 VisualPrefab 脚本(PenCollisionFeedback / 效果类)在碰撞回调里引用 `Camera.main` 等主场景单例的副作用——对预测结果无影响,但可能触发额外的反馈调用(性能不敏感时可忽略)。
- `TrajectorySimulator.ApplyLaunch` 与 `PenEntity.Launch` 数学一致,但若后者未来修改(如新增 OnLaunch 效果),须同步 `TrajectorySimulator.ApplyLaunch`。

---

## 常见错误与排查

| 症状 | 可能原因 | 排查步骤 |
|------|---------|---------|
| Console 报 `[PhysicsMirrorWorld] 初始化失败` | `sourcePen` 或 `tableCollider` 未赋;或 `PenEntity.penCollider` 为 null(Assembly 未跑) | 确认 Inspector 两字段已赋;确认进入战斗时 `PenEntity.penCollider` 非空(即 `PenAssembly.BuildBattleView` 已执行) |
| 拖拽时完全看不到预览 | `enablePreview=false` / `disclosureLevel=Minimal` / 镜像初始化失败 / `LaunchForce` 始终 < `minForceToPredict` | 勾 `enablePreview`;切 `Trend` 或 `Full`;看 Console;鼠标实际拖动足够距离 |
| 仅见方向带,没有 ghost pen | `sourcePenForGhost` 未赋;或主场景 PenAssembly 未构建(Assembly.BarrelData 为 null)导致 Presenter 跳过本次构建 | 补赋 SerializeField;确认运行时玩家笔装配已完成(进入战斗阶段后);更换装配后需要重启预测系统才能看到新 ghost(首版不自动重建) |
| Full 模式折线抖动/撕裂 | `throttleMs` 过短导致每帧重仿真与 `SampleStride` 不匹配;或镜像与主场景物理参数漂移 | 调大 `throttleMs` 到 60–80;确认装配变更后 `NeedsRebuildPen` 日志无异常 |
| 预测线偏离实机 | 台面 AABB 近似;或 `PenEntity.Launch` 逻辑改过但 `TrajectorySimulator.ApplyLaunch` 未同步 | 临时切 Full 对比录像;若语义确有漂移,同步两处 Launch 数学 |
| **预测落点远大于实机**(笔像"没摩擦"一路滑出台面) | `rb.position` setter 不自动同步 transform → 首帧碰撞检测用旧位置 → 镜像笔穿台面自由落体 → 无摩擦飞远 | 已在 `SyncFrom` 末尾补 `Physics.SyncTransforms`;若仍异常,确认镜像笔 `collisionDetectionMode = ContinuousDynamic` 未被覆盖,且 `tableCollider.bounds` 实际覆盖玩家起始位置 |
| **预测笔在镜像中翻滚 / 自旋过度** | 主场景 `Rigidbody.constraints` 锁了 X/Z 旋转,镜像未同步 | `PenSnapshot` 已带 `Constraints` 字段,`ApplyPlayerDynamics` 每次同步;若仍翻滚,确认主场景玩家笔 Rigidbody 的 Freeze Rotation 勾选符合预期 |
| 点击玩家笔瞬间 Console 报 `Can't remove Rigidbody/PenEntity/PenAssembly because X depends on it` | Ghost pen 克隆时删除组件与 `[RequireComponent]` 依赖链冲突(`PenCOMVisualizer` → `Rigidbody/PenAssembly`;`PenCollisionFeedback` → `PenEntity` 等);`GetComponentsInChildren<MonoBehaviour>` 顺序不保证拓扑 | 已改为 **不删除、全部禁用**:MB `enabled=false` + Rigidbody `isKinematic=true` + Collider `enabled=false`;若仍出现,确认 ghost 构建代码未被其他 Editor 脚本覆盖 |
| 装配换零件后首帧预测失效 | `NeedsRebuildAssembly` 按 `BarrelData` + `AssembledParts`(逐项 Data/Socket) 比对;装配变化会 `ClearBattleView` + `SetData` + `BuildBattleView` 在镜像场景重建 | Profiler / Hierarchy 观察镜像 `MirrorPen` 子节点是否已按新装配重新 Instantiate;质量/COM 由 `PenPhysicsAggregator.Recalculate` 同路径得到 |
| 敌方笔在镜像中看起来位置不对 | 敌方 `sourceEnemyPen` 未赋值,或主场景敌方位姿被外部代码移动后未与镜像同步 | 每次 SyncFrom 都会重置镜像敌方位姿为主场景当前值;若 sourceEnemyPen 为 null,镜像无敌方刚体,玩家笔可直接穿过(预测范围不含敌方) |
| 敌方笔纯模型 / 没有物理装配 | 敌方 `PenAssembly.BarrelData == null`(只挂了视觉) | `BattleStateMachine.TryEnsureEnemyAssembly` 会在 Update 首次兜底用玩家默认装配补建敌方 `PenAssembly`;若仍失败,确认 Console 有 `[BattleStateMachine] 敌方笔用玩家默认装配补建完成` 日志 |
| 切换到 `OnCollide` 级别没有看到折线截断 | 本次预测未发生玩家-敌方碰撞(`CollisionOccurred=false`) | OnCollide 在无碰撞时**退化为 Full 玩家侧表现**(全程折线 + 玩家 ghost 最终位姿);拖拽方向瞄准敌方以触发碰撞 |
| 切换到 `Full` 级别后预测时间明显变长 | 敌方笔被撞飞后还在动,Full 停止判据要等双笔都静止 | 正常;受 `MirrorSimulationConfig.MaxTime`/`MaxSteps` 硬截断约束,必要时调低 MaxTime |
| Full 级别 ghost 敌方笔未出现 | `sourceEnemyPenForGhost` 未赋;或敌方装配补建失败 | Inspector 核对;确保本轮 Unity 打开后 `BattleStateMachine.TryEnsureEnemyAssembly` 日志已出 |
| 预测路径穿过墙壁 / 桌脚 / 障碍物 | 这些物体未纳入镜像(`PhysicsMirrorWorld.extraColliders` 为空或漏) | 在 Inspector 为 `extraColliders` 逐个拖入对应 Collider;运行时检查镜像 Scene 里有对应 `MirrorExtra(*)` 子物体 |
| 敌方被撞飞预测的距离/角度与实际差距明显 | 敌方 rb 动态属性(mass/COM/damping)未同步到镜像,或零件 OnLaunch 持续力在主场景跑但镜像不跑 | 确认代码已包含敌方完整 rb 拷贝(`mass/COM/damping/useGravity/constraints` 在 SyncFrom 敌方分支);若零件带持续力脚本,GDD §3.3 已说明 "不保证完全一致" |
| Idle 初帧瞬间闪现一次上次的预测 | `onlyShowWhileDragging` 关闭,`ctx.LaunchForce` 仍保留上次蓄力值 | 勾选 `onlyShowWhileDragging`(默认已勾);或在 `CancelDrag` 路径外也清 `ctx.LaunchForce`(当前 `FinishDrag` 不清) |
| Hierarchy/画面里能看到"镜像笔"与玩家笔/敌方笔叠加显示 | Unity 主相机渲染 **所有已加载 Scene 的 Renderer**,镜像笔 barrel/零件的 MeshRenderer 也在其中 | 已修:`PhysicsMirrorWorld` 构建/重建双笔副本末尾 `DisableAllRenderers`;若手工给镜像 GO 加了 Renderer,自行保持 `enabled=false` |
| 预览在 `ActionState` 仍显示 | `BattleStateMachine.IsIdle` 未返回 false(状态机异常) | 确认进入 `ActionState` 后 Controller 下一帧调用 `HideNow`;若不是,检查状态机迁移代码 |
| 镜像 Scene 在 Edit Mode 里被留下 | 只在 Play Mode 创建;若异常中断或 `OnDestroy` 没跑,下次 Play 会重新建 | 正常情况无需干预;若 Hierarchy 残留可手动 Unload |

---

## 更改日志

| 日期 | 摘要 |
|------|------|
| 2026-04-22 | 初版:按 CLAUDE.md 固定章节顺序建立;参数表与 `implementation/trajectory-prediction.md` 的「实际代码层」Inspector 清单逐项对齐,新增「影响」「推荐值」列;给出 `Prediction.unity` 最小可复现挂载步骤与排错表 |
| 2026-04-22 | 新增「一键配置(推荐)」小节,基于 `Assets/Scripts/Editor/PredictionSceneSetup.cs`(`[InitializeOnLoad]` + `Tools/Trajectory/Setup Prediction Scene` MenuItem);手动步骤降为备选,并声明自动查找策略(sourcePen/tableCollider)与 SO 资产生成路径 |
| 2026-04-22 | 方案加强同步:`PhysicsMirrorWorld` 参数表新增 `sourceEnemyPen`(双笔完整 PenAssembly 装配副本);已知偏差更新镜像结构不再是单 CapsuleCollider 近似;常见错误加装配 diff 重建排查项与敌方笔位姿同步说明;`IncludeEnemyPen` 字段标为已无效 |
| 2026-04-22 | 常见错误追加"预测落点远大于实机"排查项(根因:`Physics.SyncTransforms` 缺失;已在 `PhysicsMirrorWorld.SyncFrom` 末尾补调,且镜像笔改为 `ContinuousDynamic` 碰撞检测) |
| 2026-04-22 | 视觉重构 #2 同步:手动步骤移除 `LandingEllipse` 子物体;参数表 Presenter 去掉 `landingEllipseRoot` / `landingEllipseHeight`,新增 `sourcePenForGhost` / `ghostMaterial`;`filterConfig` 去掉 `TrendLandingBaseRadius` / `TrendLandingRadiusPerSecond`;常见错误改为"仅见方向带,没有 ghost pen"排查项 |
| 2026-04-22 | Bug 修复:常见错误新增"预测笔在镜像中翻滚"(根因:`Rigidbody.constraints` 未同步,已在 `PenSnapshot` 补字段)与"点击笔 `Can't remove ... depends on it`"(根因:Ghost pen 克隆 `Destroy` 与 `[RequireComponent]` 冲突,改 `DestroyImmediate` + MB→Rigidbody→Collider 顺序) |
| 2026-04-22 | Bug 修复再进:`DestroyImmediate` 顺序仍不可控(`GetComponentsInChildren` 顺序无拓扑保证),依赖链报错仍可能 → 改为 ghost 全组件 **禁用不删除**(MB `enabled=false` + Rigidbody kinematic + Collider disabled) |
| 2026-04-22 | #3 敌方装配同步:新增常见错误"敌方笔纯模型"排查项;相关脚本清单加 `TrajectoryCollisionProbe.cs` |
| 2026-04-22 | #4 OnCollide + Full 扩展同步:手动步骤第 4 项加 `sourceEnemyPenForGhost`;参数表 Presenter 加 `sourceEnemyPenForGhost`;Controller `disclosureLevel` 范围改为 Minimal/Trend/OnCollide/Full;常见错误加"OnCollide 无碰撞退化"、"Full 时间变长"、"Full 敌方 ghost 未出现"三项 |
| 2026-04-22 | 精度增强 + 交互开关:参数表 `PhysicsMirrorWorld` 加 `extraColliders`,Controller 加 `onlyShowWhileDragging`;手动步骤 3 补 `extraColliders` 填写;常见错误加"预测穿过墙壁/桌脚"、"敌方被撞飞差距明显"、"Idle 初帧闪现"三项 |
| 2026-04-22 | Bug 修复:常见错误加"镜像笔与主场景叠加渲染"排查项;修复方式为镜像双笔构建末尾 `DisableAllRenderers`(仅保留物理,无视觉) |
