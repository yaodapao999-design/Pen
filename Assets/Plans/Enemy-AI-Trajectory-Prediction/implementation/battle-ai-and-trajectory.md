# 实现计划：对手 AI 与弹射物理预测指示

**上级文档**：[`../game-design.md`](../game-design.md)

本文档在 GDD 冻结意图后，收敛为可编码结构；落地前应与 `game-design.md` 同步更新「更改日志」。

---

## 背景

战斗层已有 `BattleStateMachine`、`IdleState` / `ActionState` / `ResultState`、`BattleContext`（`LaunchDirection`、`LaunchForce`、`ContactPointWorld`）与 `PenEntity.Launch`。当前 `ActionState` 仅发射玩家笔，且无存档式「敌方回合」与预测 UI。需在不大改胜负语义的前提下扩展行动方与表现系统。

---

## 目标

- 引入 **回合制行动方**，使敌方可在其回合提交与玩家同构的弹射参数并进入统一发射流程。
- 提供 **蓄力阶段** 的轨迹/趋势预测指示，级别与 GDD §4 一致（实现从 L0 或 L1 起步均可，须在脚本与 `editor-setup.md` 中标注）。
- 核心评分/预测逻辑尽量 **可注入、可单测**（纯 C# + 接口抽象时间与随机源，见 `Coding-Guide.md`）。

---

## 非目标

- 不改变 `GameManager` 外层阶段（Shop/Workshop/Battle）职责划分，除非最小改动无法接线。
- 首版不要求网络同步、回放确定性。
- 不要求首版即实现 GDD §4 的完整 **L2**；若不做 L2，不得在 UI 文案中暗示「完全精确」。

---

## 方案概述

1. **行动方**：在 `BattleContext` 或平行服务中增加 `CurrentActor`（`Player` / `Enemy`）与 `TryBeginLaunch` / `CommitLaunch` 语义；`ActionState` 对 `GetActivePen()` 调用 `Launch`。
2. **敌方 AI**：`IEnemyLaunchPlanner` 在敌方 Idle 子阶段生成 `LaunchCommand`；经校验后写入 `BattleContext` 并切换 `ActionState`（或触发等价事件）。
3. **预测**：`ITrajectoryPredictor` 实现 L0/L1；可选 `PhysicsScene` 克隆或 `Physics.Simulate` 做 L2。表现层 `TrajectoryPreviewView` 只消费预测折线/网格数据。

详细类型与路径见下文「可编码的代码结构」与「实际代码层」；**脚本尚未存在时，本节为拟议契约，落地后以仓库 `.cs` 为准并回写本节**。

---

## 可编码的代码结构

### 命名空间

- 默认与现有脚本一致：**全局命名空间**（与 `PenEntity`、`BattleStateMachine` 一致）；若引入子文件夹 `Assets/Scripts/BattleAI/`，是否加 `Pen.Battle` 命名空间由首次落地 PR 决定并在本文更新。

### 类型职责（拟议）

| 类型 | 职责 |
|------|------|
| `BattleActor` | 枚举：`Player`, `Enemy` |
| `LaunchCommand` | 只读结构体：`Direction`, `Force`, `ContactPointWorld` |
| `BattleTurnService` 或并入 `BattleContext` | 维护当前行动方、切换回合、查询当前可操控 `PenEntity` |
| `EnemyLaunchPlanner` | 采样/评分生成 `LaunchCommand`；依赖 `ITrajectoryPredictor`（可选）与只读场景几何 |
| `TrajectoryPredictorL0` / `L1` | 实现 `ITrajectoryPredictor` |
| `TrajectoryPreviewPresenter` | LineRenderer / DebugDraw / UIElements：消费预测点列 |

### 数据流（简述）

`IdleState`：若当前为敌方 → 驱动 `EnemyLaunchPlanner` → 得到 `LaunchCommand` → 写入 Context → `ActionState`。若为玩家 → 现有拖拽输入 + 可选调用 `TrajectoryPreviewPresenter` 刷新。

---

## 实际代码层（CS 脚本）

> **状态**：规划稿。首次实现后必须在此列出每个 `.cs` 的路径、`public`/`SerializeField` API，并与 `editor-setup.md` 对齐。

### 拟新增或修改的文件（清单占位）

| 路径 | 动作 | 说明 |
|------|------|------|
| `Assets/Scripts/BattleState/ActionState.cs` | 修改 | 发射目标改为「当前行动方笔」 |
| `Assets/Scripts/BattleState/IdleState.cs` | 修改 | 接入回合与敌方规划触发 |
| `Assets/Scripts/BattleState/BattleContext.cs` | 修改 | 行动方、`LaunchCommand` 缓存（若不放独立服务） |
| `Assets/Scripts/BattleStateMachine.cs` | 可能修改 | 暴露当前行动方或事件（按需） |
| `Assets/Scripts/BattleAI/*.cs` | 新增 | Planner、策略 SO、难度配置（路径待首次 PR 确定） |
| `Assets/Scripts/Trajectory/*.cs` | 新增 | 预测接口与实现、表现层 |

### Inspector 字段清单（占位）

待脚本创建后，为每个组件补全：**名称、类型、默认值、范围、影响、推荐值**。

---

## 变更清单（按文件路径）

- `Assets/Scripts/BattleState/**`：回合与发射归属。
- `Assets/Scripts/BattleAI/**`（新建）：敌方规划。
- `Assets/Scripts/Trajectory/**`（新建）：预测与预览。
- `Assets/Prefabs/**`：预览对象、可选 AI 挂载点（**需改 Prefab 时在本清单勾选并在 Checklist 中注明已获用户授权或已点名路径**）。
- `Assets/Scenes/**`：串场景引用（同上授权策略）。

---

## 风险与回滚

- **风险**：`ActionState` 与拖拽输入耦合假设被打破 → 用最小分支（`GetLaunchingPen()`）隔离。
- **回滚**：功能开关 `ScriptableObject` 或 `#if`/序列化 bool「启用敌方 AI」，默认关以便对照测试。

---

## 测试计划

1. **单元测试**（若引入 Test Runner）：`LaunchCommand` 校验、L1 阻尼下单调性、回合切换不变量。
2. **PlayMode**：玩家独奏关 AI 与开 AI 各跑 3 局；敌方连续 10 次发射无错误日志。
3. **目测**：预测线在蓄力全行程无闪烁撕裂；L2 若启用，与实机初段轨迹抽样对比录像。

---

## Checklist

- [ ] `BattleActor` / 回合切换与 `IdleState` / `ActionState` 接线完成
- [ ] `EnemyLaunchPlanner` 最小可玩路径（随机合法解 + 可选启发式）
- [ ] `TrajectoryPredictor` L0 或 L1 与 `TrajectoryPreviewPresenter` 蓄力阶段刷新
- [ ] 每个新增/修改 `.cs` 已在本文「实际代码层」补全契约
- [ ] Unity 集成：场景/Prefab 引用（若需编辑 `.unity/.prefab`：**用户已授权或已点名路径**）
- [ ] 测试计划对应项已执行或记录豁免原因
- [ ] `../game-design.md` 与 `../editor-setup.md`（创建后）参数与文案同步
- [ ] 本文件「更改日志」已更新

---

## 更改日志

| 日期 | 摘要 |
|------|------|
| 2026-04-20 | 初版：与 GDD 配套的实现计划骨架；待脚本落地后补全 CS 契约与 editor-setup |
