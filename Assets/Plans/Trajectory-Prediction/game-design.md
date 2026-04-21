# GDD:弹射物理预测指示

本文档定义 **Pen**(桌面物理对战:拖拽弹射钢笔、冲量与旋转、击落对手)中 **玩家弹射前的轨迹/落点预测表现** 的设计意图、规则边界与验收标准。实现细节见本目录下实现计划。

**关联项目总览**:`../Project-Overview.md`(战斗状态机 `Idle → Action → Result`、`BattleContext`、`PenEntity.Launch` 等)。

> **范围说明**:本 GDD 仅聚焦 **弹射物理预测指示**。对手 AI 作为后续独立 GDD 立项,与本文件无耦合。

---

## 1. 设计目标

- 在 **玩家处于可弹射的交互状态**(通常为 `IdleState` 下的拖拽蓄力阶段)时,提供 **物理正确、低误导** 的预测反馈,帮助玩家理解「施力点 + 方向 + 力度」与可能轨迹的关系。
- **所有预测一律来自主场景的物理镜像**(独立 `PhysicsScene` + 同步的刚体/碰撞体快照);不使用任何解析近似、启发式折线或质点抛物近似。预测与实机的差异只可能来自帧间/随机因素,不来自「方程不同」。
- **玩家感知到的预测差异 = 披露多少镜像结果,而非算法不同**。三档披露级别仅决定表现层暴露的信息密度(见 §3),底层仿真保持完全一致。
- 预测表现与当前拖拽/蓄力 UI 共存协调,不抢戏、不遮挡关键信息;释放后即时收起,避免残留误导。

---

## 2. 核心玩法语境(与现有代码对齐)

| 概念 | 说明 |
|------|------|
| 双笔实体 | `PenEntity`:玩家 `pen`、对手 `enemyPen`(`BattleContext` / `BattleStateMachine`) |
| 弹射 | `PenEntity.Launch(direction, force, contactPointWorld)`:`AddForceAtPosition` 冲量 + 零件弹射倍率 + 笔身恒定阻力(`FixedUpdate`) |
| 战斗子状态 | `IdleState`(蓄力发生在此)→ `ActionState` → `ResultState` |
| 预测作用域 | 玩家按下拖拽到释放之间的 **蓄力阶段**;进入 `ActionState` 后停止刷新 |
| 物理镜像 | 独立的 `PhysicsScene`,持有主场景关键刚体/碰撞体的同步副本,用于对候选 `LaunchInput` 步进出可消费的轨迹数据 |

本 GDD 不改变胜负与状态机外壳,仅在 **表现层** 上扩展。

---

## 3. 预测方案:统一镜像 + 披露分级

### 3.1 统一底层:物理镜像

**预测的唯一实现路径是物理镜像:**

1. 在独立 `PhysicsScene` 中持有 **双笔完整装配副本**(玩家笔 + 敌方笔,均通过 `PenAssembly.InitData + BuildBattleView` 在镜像场景 1:1 复刻 barrel + 所有子零件的 Collider / `PhysicsMaterial` / 质量与质心聚合)、台面副本与 **额外静态物理元素**(桌脚 / 墙壁 / 凸起 / 障碍物等,Inspector 配置的 `extraColliders` 列表,原 GameObject 克隆到镜像 Scene 保留 Collider/PhysicsMaterial,禁用 MB/Renderer)。不允许把笔简化为单一胶囊近似。双笔的动态 Rigidbody 属性(mass / COM / 阻尼 / constraints)均每帧从主场景完整同步,避免敌方 mass 漂移导致碰撞动量失真。
2. 每次预测前,镜像从主场景 `PenAssembly` 读取最新 `BarrelData` + 零件列表;装配变化(换杆 / 换零件)自动触发镜像重建。
3. 对候选 `LaunchInput`(方向、力度、施力点)在镜像中执行与主场景 **等价的 `Launch` 调用**,用固定步长 `PhysicsScene.Simulate` 步进至终态(或达步数/时间/速度阈值上限)。
4. 采样得到完整 `TrajectorySample`(位姿序列、速度、最终停靠点、停止原因)。此采样即「预测真值」,表现层不再对其做物理重算或近似。

这避免了解析近似带来的 UI/实机不一致;成本代价由「节流、单例镜像复用、步数上限、快照失效标记」控制(详见实现计划)。

### 3.2 披露分级(玩家契约)

玩家感知到的「预测精度」仅取决于 **披露多少镜像结果**,底层仿真不变:

| 级别 | 披露内容 | 玩家契约 |
|------|---------|----------|
| **D0 Minimal** | 仅显示施力点、拖拽向量、力度条;不展示任何镜像采样结果 | 「操作反馈」,不承诺任何落点信息 |
| **D1 Trend** | 披露初段方向指示带 + 玩家 ghost pen(最终位姿);**不显示中段折线** | 「方向与落点大致姿态」 |
| **D1.5 OnCollide** | 方向带 + 折线(**截至玩家与敌方首次碰撞时刻**)+ 玩家 ghost pen(碰撞瞬间位姿);若本次预测未发生碰撞,退化为 Full 的玩家侧表现 | 「到碰撞瞬间为止的完整披露」,帮助玩家规划攻击站位 |
| **D2 Full** | 方向带 + 完整折线 + 玩家 ghost(最终)+ **敌方 ghost(被撞飞后的最终位姿)**;预测终止条件为 **所有笔(含敌方)都静止 / 掉落 / 超时** | 「所见即所得」,含敌方被撞后的静止位置;仅免责于帧间随机差异 |

- 同一次蓄力可在级别间切换(设置/难度旋钮),**不触发重新仿真**——仅改变表现层的遮罩/裁剪。
- 披露级别的默认选择与「观感作弊」的权衡是设计问题(见 §8 开放问题),不是实现问题。

### 3.3 非目标(预测)

- 里程碑内 **不承诺** 预测考虑:
  - **笔-笔碰撞的动态演化**:双笔物理体均完整装配至镜像,`PhysicsScene` 自身处理玩家笔预测路径上与敌方笔的碰撞;敌方笔在蓄力阶段按 **静止快照** 进入镜像,被撞后 **在 Full 级别会继续演化直到静止**(Full 的玩家契约含敌方最终位姿);Trend / OnCollide 不披露敌方动态。
  - **零件 VisualPrefab 上的脚本副作用**:镜像中零件实例经 `Instantiate` 会触发其 `Awake`;若其携带改变物理力/扭矩的组件(如 `RocketEffect` 持续推力),**预测不刻意剔除也不保证与实机完全一致**——视为零件本身的一部分。感觉反馈类脚本(`PenCollisionFeedback` 等)在镜像中 Awake 但不产生可见副作用。
  - **台缘复杂摩擦 / 随机掉落**:镜像与主场景共享 `PhysicsMaterial` 资产,但不保证边缘掉落概率完全一致。
- 不承诺确定性回放或网络同步。

---

## 4. 预测指示:表现规格

### 4.1 视觉(按披露级别)

**落点表达统一**:不再使用椭圆/色带表示落点,改为在最终位姿处放置一个 **半透明笔幽灵(ghost pen)**——与主场景对应笔同装配的视觉克隆、材质替换为半透明,直接呈现笔最终停止位置 **+ 朝向**。椭圆无法反映旋转姿态;笔身朝向对"会不会掉下台面 / 倾向哪一端"至关重要,故此项升级到所有可见披露级别共享。Full 级别额外展示敌方 ghost(被撞后的敌方笔最终位姿)。

- **D0**:仅渲染施力点高亮 + 拖拽向量 + 力度条;无轨迹元素,也无 ghost pen。
- **D1**:从施力点沿初速度方向渲染 **方向指示带** + 终点 **玩家 ghost pen**(最终位姿);**不显示中段折线**。
- **D1.5 OnCollide**:方向带 + 折线(截到玩家与敌方首次碰撞的时刻)+ **玩家 ghost pen(碰撞瞬间位姿)**;**不显示敌方 ghost**(碰撞瞬间敌方还未动,主场景本身可见)。未发生碰撞时,折线与 ghost 退化到 Full 的玩家侧表现。
- **D2 Full**:完整 LineRenderer 折线 + 终点 **玩家 ghost pen**(最终)+ **敌方 ghost pen**(被撞后的最终位姿,半透明);预测步进到 **双笔都静止** 或 MaxTime / MaxSteps 截断。

### 4.2 刷新与性能

- 拖拽连续变化时 **节流**(每 N ms 或位移超阈值重算);镜像步进须有 **步数/时间上限** 与速度停止阈值。
- 释放 / 离开 Idle → 立即收起所有预测 UI,不残留;`ActionState` 期间不再重算预测。
- 披露级别切换 **复用最近一次 `TrajectorySample`**,仅重走表现层,避免重仿真。

### 4.3 可访问性

- 色觉友好:预测线不只依赖红绿对比;可用线型/脉冲动画辅助区分「预测」与「场景实体」。
- 允许玩家通过设置关闭预测(等同 D0)以降低视觉噪音。

---

## 5. 与改装/商店的关系

- **零件改变质量与弹射倍率**:镜像直接复用 `PenAssembly.InitData + BuildBattleView` 构建流程,**`PenPhysicsAggregator` 同路径聚合** barrel 与各零件的质量 / COM / 接触 `PhysicsMaterial`,与装配界面运行时结果 1:1 等价(而非独立 snapshot 参数)。
- **新零件效果**:零件 VisualPrefab 中的 Collider / PhysicsMaterial / Mass 自动随装配进入镜像;若零件还引入额外力/扭矩脚本(如 RocketEffect),该脚本在镜像中也会运行,`PhysicsScene` 会自然纳入其影响——但与实机是否完全对齐取决于脚本本身对外部状态的依赖(见 §3.3)。

---

## 6. 验收标准(GDD 层)

- 蓄力阶段预测反馈可见;表现随披露级别在 D0/D1/D2 间切换正确;底层仿真始终为镜像,**不允许存在解析近似作为 fallback**。
- 镜像与主场景对同一 `LaunchInput` 的轨迹 **前 N 步位姿误差 < ε**(具体 N、ε 在实现计划 / 测试计划约定)。
- 释放后(进入 `ActionState`)预测 UI 立即收起或淡出,不残留误导。
- **回归**:关闭预测(等同 D0)时的玩家流程与改动前一致。

---

## 7. 实现计划(链接)

本 GDD 对应的实现计划(脚本契约、文件树、测试与 Checklist):

- [`./implementation/trajectory-prediction.md`](./implementation/trajectory-prediction.md)

---

## 8. 风险与开放问题

| 风险 | 缓解方向 |
|------|----------|
| 镜像与主场景物理参数漂移 | 单一真源:从运行时 `Rigidbody` / `PenEntity` / `PenAssembly` 读取快照;装配变更后失效标记,下次 Predict 前强制 Sync |
| 镜像步进成本过高 | 节流;单例镜像复用;步数/时间上限;必要时分帧或简化副本集合 |
| 镜像层/标签/`PhysicsMaterial` 与主场景不同步 | 初始化时深拷贝必要材质与 Layer;自动化测试覆盖 |
| 披露级别选择引发「观感作弊」讨论 | 设计决策:D2 优先作为训练/辅助开关,竞技默认 D0/D1;文档显式声明 |
| 双笔同时运动时的预测 | 默认只预测「当前发射笔」;对方视为静态快照 |
| 镜像初始化失败(平台/API) | 退化到 D0(等同关闭),**不退化到解析近似** |
| 镜像装配重建成本(换装 / 换杆时重建双笔) | 暂不考虑性能(GDD 本轮显式说明);成为瓶颈再引入增量重建或零件 diff |
| Full 级别预测时长增加(敌方被撞飞后仍在动) | `MirrorSimulationConfig.MaxTime` / `MaxSteps` 硬截断;必要时可配不同级别用不同 config |
| 镜像漏掉主场景某些静态物理元素(桌脚 / 墙壁 / 障碍物) → 预测穿过 | Inspector `PhysicsMirrorWorld.extraColliders` 显式列表补齐;若新增环境物体,须同步更新此列表 |

**开放问题**:不同难度/模式下的默认披露级别;是否暴露「预测命中偏差」统计给玩家自省;是否提供开发者 Debug 视图直接可视化镜像场景。

---

## 更改日志

| 日期 | 摘要 |
|------|------|
| 2026-04-20 | 初版:对手 AI 回合制 + 预测 L0–L2 分级,与现有战斗/物理语义对齐 |
| 2026-04-21 | 范围收窄:移除对手 AI 相关章节(§3 回合与控制权、§5 敌方 AI 行为规格、§8 AI 验收项等),GDD 仅聚焦弹射物理预测指示;章节重新编号 |
| 2026-04-21 | 目录重命名为 `Trajectory-Prediction/`;实现计划文件重命名为 `trajectory-prediction.md`,GDD 链接同步更新 |
| 2026-04-21 | 方案重构:废止 L0/L1 解析近似,底层统一为物理镜像(独立 `PhysicsScene`);原「精度分级 L0/L1/L2」改为「披露分级 D0/D1/D2」——底层同一仿真,差异只在表现层裁剪 |
| 2026-04-22 | 里程碑:实现计划首版代码落地(详见 `implementation/trajectory-prediction.md`);GDD 参数未变动 |
| 2026-04-22 | 方案加强:镜像废止「单 CapsuleCollider + bounds 近似」,改为 **双笔完整装配副本**(走 `PenAssembly.InitData + BuildBattleView` 在镜像场景 1:1 构建 barrel + 零件的 Collider/材质/质量);§3.1 / §3.3 / §5 / §8 相应更新,笔-笔碰撞从「不承诺」改为「物理体存在、动态演化不纳入预测」 |
| 2026-04-22 | Bug 修复 #1(预测距离偏远):补 `Physics.SyncTransforms` 并将镜像笔 `collisionDetectionMode` 设为 `ContinuousDynamic`,防 `rb.position` 更新后首帧穿透台面导致无摩擦飞远(GDD 无设计变更,仅记录里程碑) |
| 2026-04-22 | 视觉重构 #2:§4.1 落点从「椭圆/色带」改为「**半透明 ghost pen**」(玩家笔的视觉克隆,半透明材质,呈现最终位姿),适用所有可见披露级别 |
| 2026-04-22 | Bug 修复:(a)镜像笔缺 `RigidbodyConstraints`(主场景锁 X/Z 旋转未同步到镜像 → 笔翻滚),`PenSnapshot` 加 `Constraints` 字段并在 `ApplyPlayerDynamics` 同步,敌方同理;(b)Ghost pen 克隆时按 `[RequireComponent]` 依赖链顺序 `DestroyImmediate`(MB → Rigidbody → Collider),解决"Can't remove Rigidbody because PenCOMVisualizer depends on it"等报错 |
| 2026-04-22 | Bug 修复再进:顺序式 `DestroyImmediate` 仍可能因 `GetComponentsInChildren` 顺序非拓扑而触发依赖链报错 → ghost 改为 **禁用不删除**(MB `enabled=false` + Rigidbody kinematic/freeze all + Collider disabled);外观保留,物理与脚本全冻结 |
| 2026-04-22 | #3 敌人装配:`BattleStateMachine.TryEnsureEnemyAssembly` 在 Update 首次兜底用玩家默认装配补建敌方 `PenAssembly`(InitData + BuildBattleView),让敌方笔在物理层面参与镜像碰撞预测 |
| 2026-04-22 | #4 新级别 `OnCollide` + `Full` 语义扩展:§3.2 表新增 D1.5 OnCollide(披露到玩家与敌方首次碰撞);D2 Full 停止判据改为 **所有笔(含敌方)都静止**,并新增 **敌方 ghost pen**(被撞后最终位姿);§3.3 非目标更新(敌方动力学在 Full 纳入)、§4.1 视觉按新分级重写、§8 风险加 Full 时长上限 |
| 2026-04-22 | 精度增强 + 交互开关:§3.1 补"额外静态物理元素(`extraColliders`)"说明 + 敌方动态属性完整同步声明;§8 风险加"镜像漏配环境物体";交互层加"只在拖拽时显示预测"开关(`onlyShowWhileDragging`)以避免 Idle 初帧残留旧 `LaunchForce` 触发瞬显 |
| 2026-04-22 | Bug 修复:主相机会渲染所有已加载 Scene 的 Renderer → 镜像笔 barrel+零件的 MeshRenderer 会与主场景叠加显示("mirror 没隐藏")。`PhysicsMirrorWorld` 在 BuildAssembledMirror / RebuildAssembly 末尾 `DisableAllRenderers`,镜像仅保留物理,无视觉 |
