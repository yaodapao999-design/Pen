# Pen 项目总览

基于 **Unity URP** 的桌面物理对战原型：通过拖拽弹射钢笔，利用冲量与旋转将对手的笔击出台面。全链路（**战斗 → 商店 → 改装车间**）可在同一 `.unity` 中联调，但**工程不设固定「主场景」**：`Scenes/` 里往往是当前里程碑正在用的功能场景或试验场景，以实际开发为准打开即可。

---

## 目录结构（`Assets/`）


| 路径                   | 说明                                                                                                       |
| -------------------- | -------------------------------------------------------------------------------------------------------- |
| `Scenes/`            | 玩法、分模块与测试用场景；无单一权威入口，随迭代增减                                                                               |
| `Scripts/`           | 游戏逻辑：阶段调度、战斗状态机、笔装配、商店、反馈等                                                                               |
| `Prefabs/`           | 游戏内可复用预制体（笔零件、桌子、抽屉、像素相机、碰撞 VFX 等）                                                                       |
| `ScriptableObjects/` | `PenPartData`、`PlayerLoadout`、零件效果等数据资产                                                                  |
| `Arts/`、`Material/`  | 美术与材质                                                                                                    |
| `Audio/`             | 音效资源                                                                                                     |
| `InputSystem/`       | 新输入系统 `InputSystem_Actions`                                                                              |
| `Settings/`          | URP 等渲染与项目设置                                                                                             |
| `Plugins/`           | 第三方：`Feel`（反馈）、`3DPixelCamera` / `3DPixelArtEnvironment`、Cinemachine 依赖包、`Critter Volumetric Lighting` 等 |
| `Plans/`             | 设计与实现文档（含本文件、`Coding-Guide.md`、`Document-Driven-Development.md`）                                         |


`Scripts/` 内大致分层：

- `**Core/`**：`GameManager`（阶段机）、`BattlePhase` / `ShopPhase` / `WorkshopPhase`、`DragInputState`、`DragPlane` 等
- `**BattleState/**`：`BattleContext`、`IdleState`、`ActionState`、`ResultState`
- `**Customization/**`：`PenAssembly`、`PartSocket`、`PenPhysicsAggregator`、效果与 `Workshop/` 下车间流程
- `**Shop/**`：`ShopController`、`ShopDrawer`、`ShopPool` 等
- `**Inventory/**`：`PlayerInventory`、`PlayerWallet`、`PlayerLoadoutSO`
- `**Feedbacks/**`：`FocusCameraController`、`PenCollisionFeedback` 等
- `**Editor/**`：场景与反馈相关的编辑器辅助脚本

---

## 场景与典型层级

做**全链路联调**时，场景中常会挂载下列对象与脚本（名称以 Hierarchy 为准，可能因场景而异）：


| 层级 / 对象（节选）                             | 作用                                                                                                                     |
| --------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **GameManager**                         | 单例；`ChangePhase` 在 **Battle / Shop / Workshop** 间切换；持有 `PlayerInventory`、`PlayerWallet`、`PlayerLoadoutSO`，启动时应用初始装配与仓库 |
| **BattleManager**（`BattleStateMachine`） | 玩家笔、对手笔；`battleRoot` 显隐战斗子树；战斗用 **Cinemachine** 相机引用；笔位置快照供阶段切换衔接                                                      |
| **Shop**                                | `ShopController`、`ShopBookAnimator`、`ShopDrawer`、`DrawerAnimator`；商店虚拟相机等                                              |
| **车间相关根物体**                             | `WorkshopController`、`WorkshopPenSpawner`、`WorkshopConfig`、`SocketHighlighter`；槽位上的 `WorkshopSlot` 等                   |
| **FocusCameraSystem**                   | `FocusCameraController` + **CinemachineTargetGroup**，战斗期跟拍双笔并调节 FOV                                                    |
| **反馈根物体**、**MMTimeManager**             | MoreMountains **Feel**（如 `MMF_Player`、`MMPositionShaker`、`MMTimeManager`）                                              |
| **玩家笔 / 对手笔**                           | `PenEntity`、`PenAssembly`、`PenCollisionFeedback`；与碰撞反馈、装配数据联动                                                          |
| **Canvas / PhaseButton**                | 阶段切换按钮与 TMP 文案；**EventSystem** 使用 **Input System** UI 模块                                                               |


内层战斗仍为 `**IdleState → ActionState → ResultState`**（由 `BattleStateMachine` 每帧驱动）；外层由 `**GameManager` + `IGamePhase**` 负责何时进入/退出战斗、商店与车间，二者边界在 `GameManager` 类注释中已写明。

---

## `Assets/Prefabs` 预制体一览


| 预制体                                                                                      | 用途（结合命名与工程用法）                                               |
| ---------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| **PenMeshTemplate**                                                                      | 笔身 FBX 层级模板（笔杆、笔芯、笔尖等子节点命名），供装配与视觉对齐参考                      |
| **Barrel_Standard / Cap_Standard / Refill_Standard / Tip_Standard / Accessory_Standard** | 各 `PartType` 对应的标准视觉与碰撞体预制体，由 `PenPartData.VisualPrefab` 引用 |
| **Table**                                                                                | 战斗台面环境                                                      |
| **Drawer**                                                                               | 商店抽屉表现（与 `ShopDrawer`、`DrawerAnimator` 等配合）                 |
| **PenPixelCameraSystem**                                                                 | 基于插件 **3DPixelCamera** 的像素渲染相机系统变体                          |
| **VFX_CollisionSparks** + **Mat_CollisionSparks**                                        | 碰撞火花粒子与材质                                                   |


---

## 数据与装配

- `**PenPartData`**（`ScriptableObject`）：零件身份、Socket、质量、物理材质、弹射倍率、价格、`**PenPartEffect[]**` 等；创建菜单 `GameData/PenPart`。
- `**PenAssembly**`：运行时根据数据构建战斗视图、聚合物理（如 `PenPhysicsAggregator`）。
- `**PlayerLoadoutSO**`：新开局时的笔杆、已装零件、初始仓库列表；由 `GameManager.ApplyLoadoutOnNewSave` 写入（注释中预留接入持久存档后的分支）。

---

## 主要第三方依赖

- **Universal Render Pipeline (URP)**：`Settings/` 下管线资产  
- **Unity Input System**：输入与 UI  
- **Cinemachine**：战斗聚焦、商店/车间虚拟相机  
- **MoreMountains Feel**：屏幕震动、反馈播放器、时间缩放等  
- **TextMesh Pro**：UI 文本  
- **3DPixelCamera / 3DPixelArtEnvironment**：像素风相机与环境资源（部分在 `Plugins/`、`Assets/3DPixelCamera`）

---

## 文档与协作

- 编码约定与工程原则：`**Assets/Plans/Coding-Guide.md`**  
- 文档驱动迭代契约：`**Assets/Plans/Document-Driven-Development.md**`、`**.cursor/rules/plan-output.md**`  
- 贡献说明：`**Assets/CONTRIBUTING.md**`

---

## 与归档说明的关系

`Assets/Plans/Archived/PROJECT_OVERVIEW.md` 为较早版本总览；**以本文件与当前仓库为准**。场景 Hierarchy 与引用随迭代变化时，以场景内实际对象为准；本文**不维护具体 `.unity` 文件名清单**，避免与「当前开发场景」脱节。