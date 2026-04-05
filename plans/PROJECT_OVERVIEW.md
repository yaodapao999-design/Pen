# 项目解读：Pen —— 钢笔弹射对战游戏 (Unity)

> 这是一个基于 **Unity URP** 的 3D 桌面物理对战游戏，核心玩法是**拖拽弹射钢笔**，将对手的笔击落桌面以获胜。

---

## 📁 项目结构总览

```
Assets/
├── Arts/               # 美术资源（2D Sprite + 3D FBX模型）
├── Feel/               # MoreMountains Feel 音效/反馈插件
├── InputSystem/        # Unity 新输入系统配置
├── Prefabs/            # 预制体（PenMeshTemplate）
├── Scenes/             # 场景（SampleScene, GameFeedback）
├── Scripts/            # 核心逻辑脚本
│   ├── BattleState/    # 状态机各状态实现
│   ├── BattleStateMachine.cs
│   ├── PenEntity.cs
│   ├── PenPartData.cs
│   ├── SpriteStackRenderer.cs
│   ├── IEntityState.cs
│   └── GameEnums.cs
├── ScriptableObjects/  # 零件数据资产（PenPartData 实例）
└── Settings/           # URP 渲染管线配置（PC + 移动端双配置）
```

---

## 🎮 核心玩法逻辑

### 状态机架构

整个战斗流程由 `BattleStateMachine` 驱动，采用经典**状态机模式**：

```
游戏开始
   │
   ▼
[IdleState] 等待状态
   │  玩家拖拽并释放鼠标
   ▼
[ActionState] 弹射状态
   │                  │
   │ 双方笔停稳        │ 任意笔掉落桌面
   ▼                  ▼
[IdleState]      [ResultState] 结算状态
                      │
                  显示胜负结果
```

### 三个核心状态

| 状态 | 文件 | 职责 |
|------|------|------|
| `IdleState` | `BattleState/IdleState.cs` | 监听鼠标拖拽，计算弹射方向 / 力度 / 接触偏移 |
| `ActionState` | `BattleState/ActionState.cs` | 执行冲量施加，轮询停止 / 掉落条件 |
| `ResultState` | `BattleState/ResultState.cs` | 判定胜负（PlayerWin / EnemyWin / Draw） |

---

## ⚙️ 弹射物理系统

`PenEntity` 是每支笔的物理实体组件，关键设计如下：

### 核心方法

| 方法 | 说明 |
|------|------|
| `Launch(direction, force, contactOffset)` | 在笔身**非质心位置**施加冲量，`contactOffset` 范围 `-1~1`，决定击打点（笔尾→中心→笔头），产生不同旋转效果 |
| `HasFallen` | 通过 `rb.position.y < fallYThreshold` 检测是否掉台 |
| `IsStopped()` | 速度 & 角速度双阈值判定笔是否停止运动 |
| `Stop()` | 强制归零速度与角速度 |
| `GetPenAxis()` | 根据 `CapsuleCollider.direction` 返回笔的轴向方向 |

### 可调参数

```csharp
[Header("弹射参数")]
float baseForce             // 基础弹射力
float stopVelocityThreshold // 速度停止阈值
float stopAngularThreshold  // 角速度停止阈值
float stopCheckDelay        // 开始检测停止的延迟时间
float maxDragDistance       // 最大拖拽距离（限制最大力度）

[Header("掉落检测")]
float fallYThreshold        // 低于此Y坐标视为掉台
```

### 拖拽输入流程（`IdleState`）

1. 射线检测鼠标是否点击到笔的 `CapsuleCollider`
2. 记录点击位置在胶囊体轴向上的偏移（归一化为 `-1~1`），保存为 `ContactOffset`
3. 将屏幕坐标投影到**笔所在的水平面**上，跟踪拖拽向量
4. **弹射方向** = 拖拽向量的**反方向**（弹弓原理）
5. **弹射力度** = 拖拽距离 / `maxDragDistance`（钳制在 0~1）

---

## 🧩 零件数据系统

`PenPartData` 是 `ScriptableObject`，定义了笔的可拆卸零件属性：

### 属性分类

| 分类 | 字段 | 作用 |
|------|------|------|
| **基础身份** | `PartID`, `DisplayName`, `Category` | 零件唯一标识与分类 |
| **防守属性** | `Mass` | 越重越难被对手撞出位移（动量守恒） |
| **进攻属性** | `LaunchPowerMultiplier` | 弹射力度倍率加成（默认 1.0） |
| **交互属性** | `PhysicMaterial` | 摩擦力（防滑）/ 弹力（反弹加成） |
| **表现属性** | `VisualPrefab`, `Icon` | 3D 模型预制体与 UI 图标 |
| **经济属性** | `BuyPrice` | 商店购买价格 |

### 零件类型枚举（`PartType`）

```csharp
public enum PartType
{
    Barrel,     // 笔杆（核心零件）
    Cap,        // 笔帽
    Refill,     // 笔芯
    Accessory   // 附件（橡胶圈等）
}
```

---

## 🎨 视觉渲染系统

`SpriteStacker` 实现了 **Sprite Stacking（精灵堆叠）** 渲染技术：

- 将多张 2D Sprite 沿 Y 轴按 `layerSpacing` 间距堆叠
- 每层旋转 `90°` 使其躺平，对齐笔的朝向
- 通过 `SortingOrder` 保证渲染层次正确
- 从特定俯视角度观察，形成**伪 3D 外观**，是一种常见的像素艺术风格技术

支持编辑器内实时预览（`[ExecuteAlways]`）和手动刷新 / 清理功能。

---

## 🔌 第三方依赖

| 插件 | 用途 |
|------|------|
| **MoreMountains Feel** | 游戏手感反馈系统（震动、音效、视觉 Juice） |
| **Unity Input System** | 新版输入系统，统一处理鼠标 / 触控输入 |
| **TextMesh Pro** | 高质量文字渲染 |
| **Universal Render Pipeline (URP)** | 通用渲染管线，支持 PC 与移动端双配置 |

---

## 📊 当前开发阶段

### ✅ 已实现

- [x] 核心弹射战斗循环（拖拽 → 弹射 → 物理结算 → 胜负判定）
- [x] 状态机架构（Idle / Action / Result 三状态）
- [x] 笔实体物理系统（冲量施加、掉台检测、停止判定）
- [x] 零件数据结构（`PenPartData` ScriptableObject）
- [x] Sprite Stacking 渲染工具
- [x] URP 多平台渲染配置

### ⬜ 待实现

- [ ] UI 界面（HUD、结算画面）
- [ ] 零件装配与切换逻辑
- [ ] 经济 / 商店系统
- [ ] AI 对手控制
- [ ] Feel 音效与视觉反馈集成
- [ ] 关卡 / 场景设计
