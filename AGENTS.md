# AGENTS.md

## Project Snapshot

**Pen** 是一个使用 **Unity 6 + URP** 开发的 low-poly 3D 俯视角斗笔游戏。它的核心玩法是物理驱动的单人回合制肉鸽：玩家用鼠标拖动自己的笔来蓄力和瞄准，松手后把笔弹射出去，尝试将敌人的笔撞出桌面/擂台，以此赢下战斗回合。

项目当前目标不是单纯的物理 demo，而是已经围绕核心玩法搭出 MVP 主循环：

- **Battle / 战斗**：玩家拖拽并弹射笔，通过物理碰撞击败敌方笔。
- **Workshop / 改装**：战斗结束后，视角从桌面移动到桌子下方抽屉；抽屉中散落着有碰撞体的笔零件，玩家可以拖拽零件到笔上进行组件化改装。
- **Shop / 商店**：战斗结束后，桌面出现记事本式商店；商品带价格，玩家把想买的组件拖进下方玩家抽屉即可购买。

这三个功能目前已经在一个 Unity 场景中组合实现，主集成场景是 `Assets/Scenes/BattleUpdate.unity`。

## Game Design Intent

The long-term game is a **single-player, turn-based roguelite pen-fighting game**. The MVP should prove that the physical pen battle, modular part customization, and shop/workshop reward loop can all work together in one readable tabletop scene.

核心设计支柱：

- **物理是玩法本体**：笔不是普通角色控制器，而是由 Rigidbody、Collider、摩擦、质量、质心、惯性张量和接触点共同决定手感。
- **组件化笔身**：笔由笔杆、笔头、笔帽、笔芯、附件等部件组成，每个部件都有数据和 prefab，并通过 socket / connector 连接到正确位置。
- **装备改变真实属性**：部件会改变整支笔的质量、质心、弹射倍率、局部/全局摩擦等属性。例如橡胶圈可能只影响接触到桌面的局部摩擦，重笔帽会改变全局质心。
- **强扩展性**：后续会加入特殊功能组件，例如电锯、口香糖等。新部件应该通过数据、效果系统、socket 和 prefab 扩展，而不是硬编码到战斗逻辑里。
- **桌面空间一体化**：战斗桌、下方抽屉、记事本商店都属于同一物理桌面场景，通过镜头/阶段切换连接，而不是分离成完全割裂的菜单。

## MVP Scope

当前 MVP 关注三件事是否闭环：

1. **战斗可玩**：玩家能拖动笔、看到瞄准反馈、发射笔，并通过物理碰撞/掉落形成胜负核心。
2. **改装可用**：玩家能进入抽屉工坊，看到随机/库存散落零件，拖拽零件并通过连接点装到笔上，退出后属性应用到战斗笔。
3. **商店可买**：玩家能在记事本商店看到带价格的组件，把组件拖入玩家抽屉完成购买，购买后的零件进入库存并能在工坊出现。

The repository is at:

```text
/Users/nmgb/Desktop/Game/Pen
```

Current Unity version:

```text
6000.3.11f1
```

## High-Level Game Loop

- **Battle**: the player drags their pen with the mouse, previews launch direction/trajectory, releases to launch, then uses collision momentum to knock the enemy pen out of the arena.
- **Post-battle choice**: after a combat round, the player can move into reward/management flows instead of staying on the table forever.
- **Workshop**: the camera travels from the tabletop to the under-table drawer. Loose physical parts are scattered in the drawer. The player drags parts onto compatible pen sockets, and the resulting assembly updates pen physics.
- **Shop**: a notebook appears on the table and opens into a shop. Each part has a price tag. Dragging a part into the player drawer purchases it and adds it to inventory.
- **Return to Battle**: the assembled pen is rebuilt into its battle view, including physical properties from its currently equipped parts.

The main integrated scene appears to be:

```text
Assets/Scenes/BattleUpdate.unity
```

Important: `ProjectSettings/EditorBuildSettings.asset` currently only lists `Assets/Scenes/SampleScene.unity`. If build/play behavior looks wrong, check whether `BattleUpdate.unity` should be added or made the active build scene.

## Important Folders

```text
Assets/Scripts/Core
Assets/Scripts/BattleState
Assets/Scripts/Interaction
Assets/Scripts/Customization
Assets/Scripts/Customization/Workshop
Assets/Scripts/Customization/Physics
Assets/Scripts/Customization/Effects
Assets/Scripts/Shop
Assets/Scripts/Inventory
Assets/Scripts/UI
Assets/Scripts/Feedbacks
Assets/Scripts/Editor
Assets/ScriptableObjects
Assets/ScriptableObjects/Channels
Assets/Prefabs
Assets/Scenes
Assets/Material
Assets/Arts
Assets/Audio
```

Third-party/plugin-heavy folders should usually be treated as external dependencies:

```text
Assets/Plugins
Assets/TextMesh Pro
Assets/TutorialInfo
Library
Temp
Logs
obj
```

Do not waste time traversing `Library/`, `Temp/`, `Logs/`, generated `.csproj` files, or generated solution files unless the task explicitly requires it.

## Core Architecture

### Phase State Machine

`GameManager` owns the outer phase flow:

```text
Assets/Scripts/Core/GameManager.cs
```

It coordinates:

- `BattlePhase`
- `ShopPhase`
- `WorkshopPhase`
- phase camera priorities
- wallet/inventory/loadout initialization
- purchase callbacks

Use `GameManager.ChangePhase`, `ToggleShop`, or `ToggleWorkshop` as the phase entry points.

### Battle State Machine

Battle behavior lives in:

```text
Assets/Scripts/BattleStateMachine.cs
Assets/Scripts/BattleState/IdleState.cs
Assets/Scripts/BattleState/ActionState.cs
Assets/Scripts/BattleState/ResultState.cs
Assets/Scripts/BattleState/BattleContext.cs
```

Current battle code is centered on the player pen. Older scenes and some serialized YAML still reference `enemyPen`, but the current `BattleUpdate.unity` path has moved toward a single-player-pen launch/respawn loop. Be careful when reintroducing enemy logic: do not assume old `BattleResult` behavior still exists.

### Pen Physics

Pen runtime physics is centered on:

```text
Assets/Scripts/PenEntity.cs
Assets/Scripts/Customization/PenAssembly.cs
Assets/Scripts/Customization/Physics/PenPhysicsAggregator.cs
Assets/Scripts/Customization/Physics/PenTableStability.cs
```

Current physics design:

- Launch uses velocity-driven impulse:

```text
impulse = direction * maxLaunchVelocity * rb.mass * force * launchMultiplier
```

- This gives all masses similar launch speed while heavier pens carry more momentum.
- `PenPhysicsAggregator` manually computes total mass, center of mass, inertia tensor, and contact materials from assembled parts.
- `PenTableStability` levels the pen while the COM is supported by the table, then lets it naturally fall when unsupported.

Avoid reintroducing the old `spinResponseFactor` / fixed impulse approach unless the task explicitly asks for that direction.

### Aim and Interaction

Aim is event-driven through:

```text
Assets/Scripts/Core/EventChannels/AimPhaseChannelSO.cs
Assets/Scripts/Core/EventChannels/AimSample.cs
Assets/ScriptableObjects/Channels/AimPhaseChannel.asset
```

`IdleState` publishes aim lifecycle events:

- began
- updated
- released
- cancelled

Subscribers include:

```text
Assets/Scripts/Interaction/PenDragInteractor.cs
Assets/Scripts/Interaction/PenDragVisuals.cs
Assets/Scripts/Interaction/PenLaunchArrowView.cs
Assets/Scripts/Interaction/PenHoverPresenter.cs
Assets/Scripts/Interaction/PenHoverVisuals.cs
Assets/Scripts/Interaction/PenPressFeedback.cs
Assets/Scripts/Interaction/PenAimThresholdFX.cs
Assets/Scripts/Interaction/AimTrajectoryPredictor.cs
```

Keep this separation: input/state publishes facts, visuals/feedback subscribe. Do not put new visual logic directly into `IdleState` unless there is a strong reason.

### Workshop

Workshop logic is substantial and should be treated as an existing system, not a scratchpad:

```text
Assets/Scripts/Customization/Workshop/WorkshopController.cs
Assets/Scripts/Customization/Workshop/WorkshopPart.cs
Assets/Scripts/Customization/Workshop/WorkshopPenSpawner.cs
Assets/Scripts/Customization/Workshop/WorkshopPartRegistry.cs
Assets/Scripts/Customization/Workshop/WorkshopPartFactory.cs
Assets/Scripts/Customization/Workshop/WorkshopSlot.cs
Assets/Scripts/Customization/Workshop/WorkshopSlotCalculator.cs
Assets/Scripts/Customization/Workshop/WorkshopConfig.cs
Assets/Scripts/Customization/Workshop/SocketHighlighter.cs
```

Key behavior:

- entering workshop creates a workshop view from `PenAssembly`
- loose inventory parts spawn into the drawer
- assembled parts snap to sockets
- replacements and invalid drops have animations/feedback
- exiting workshop writes assembly data and loose inventory data back, then rebuilds the battle view

`WorkshopPartRegistry` is the preferred lookup mechanism. Avoid broad `FindObjectsByType` scans in hot paths.

### Shop

Shop logic lives in:

```text
Assets/Scripts/Shop/ShopController.cs
Assets/Scripts/Shop/ShopPart.cs
Assets/Scripts/Shop/ShopPool.cs
Assets/Scripts/Shop/ShopDrawer.cs
Assets/Scripts/Shop/ShopPriceTag.cs
Assets/Scripts/Shop/ShopBookAnimator.cs
Assets/Scripts/Shop/ShopCoinFlight.cs
Assets/Scripts/Shop/ShopFloatingText.cs
```

Key behavior:

- `ShopPool` does weighted no-replacement rolls.
- `ShopPart` handles drag, reject, return, purchase fall, landing, and timeout fallback.
- Purchases go through `GameManager.OnPartPurchased`.
- `PlayerWallet` handles coin changes and `CurrencyHUD` listens for UI refresh.

### Player Data

Data ScriptableObjects:

```text
Assets/Scripts/Inventory/PlayerInventory.cs
Assets/Scripts/Inventory/PlayerWallet.cs
Assets/Scripts/Inventory/PlayerLoadoutSO.cs
Assets/ScriptableObjects/PlayerLoadout.asset
Assets/ScriptableObjects/PlayerInventory.asset
Assets/ScriptableObjects/PlayerWallet.asset
Assets/ScriptableObjects/ShopPool.asset
```

`PlayerLoadout.asset` is the current new-game source of truth. At the time this file was written, it gives the player:

- standard barrel
- standard cap
- standard tip
- standard refill
- standard accessory
- 15 starting coins

`PlayerInventory` and `PlayerWallet` use session snapshot restore behavior so Play Mode changes do not pollute the asset file.

### Feedback

Feedback uses MoreMountains Feel and Cinemachine:

```text
Assets/Scripts/Feedbacks/PenCollisionFeedback.cs
Assets/Scripts/Feedbacks/PenLaunchFeedback.cs
Assets/Scripts/Feedbacks/FocusCameraController.cs
Assets/plans/feel-feedback-architecture.md
```

`PenCollisionFeedback` currently uses `OnCollisionEnter` as a temporary trigger source and includes TODO comments about moving to battle-logic events later. It can drive collision feedback, FOV punch, slow motion gating, particles, audio, and hit stop if the scene MMF_Player is configured.

## Data Assets and Prefabs

Standard part data:

```text
Assets/ScriptableObjects/Barrel_Standard.asset
Assets/ScriptableObjects/Cap_Standard.asset
Assets/ScriptableObjects/Tip_Standard.asset
Assets/ScriptableObjects/Refill_Standard.asset
Assets/ScriptableObjects/Accessory_Standard.asset
```

Standard part prefabs:

```text
Assets/Prefabs/Barrel_Standard.prefab
Assets/Prefabs/Cap_Standard.prefab
Assets/Prefabs/Tip_Standard.prefab
Assets/Prefabs/Refill_Standard.prefab
Assets/Prefabs/Accessory_Standard.prefab
```

Important scene/table prefabs:

```text
Assets/Prefabs/Table.prefab
Assets/Prefabs/Drawer.prefab
Assets/Prefabs/PenPixelCameraSystem.prefab
Assets/Prefabs/VFX_CollisionSparks.prefab
```

## Current Known Caveats

- `Assets/plans/PROJECT_OVERVIEW.md` is partly outdated. It still describes older enemy/result/SpriteStacker assumptions and says UI/shop/workshop are pending, while code has moved beyond that.
- Several old scenes still have serialized `enemyPen` references. The current script no longer exposes the same enemy workflow.
- `PenEffectRunner` and `PenPartEffect` are present but appear not fully wired into launch/collision/rebuild flow yet. Check call sites before assuming part effects are active.
- There are many untracked screenshots in `Assets/Screenshots`. Confirm whether they should be versioned before committing.
- `Assets/Fonts/NotoSansSC-Regular SDF.asset` has a large diff. Treat it carefully; it may be generated/import-state churn.

## Coding Rules for This Project

- Prefer existing architecture over new global managers.
- Keep battle state logic pure and small; use event channels for visuals and feedback.
- Keep `PenEntity` focused on physics entry points, not UI or feedback.
- Keep `PenAssembly` as the source of assembled battle view data.
- Keep `WorkshopController` responsible for workshop view lifecycle and writeback.
- Do not edit third-party plugin code unless the task explicitly targets it.
- Do not commit generated Unity solution/project churn unless explicitly needed.
- Use Unity serialization-friendly patterns: `SerializeField` for scene wiring, ScriptableObjects for reusable data, and small MonoBehaviours for scene-facing logic.
- Avoid introducing new packages without user approval.

## Validation Workflow

When Unity MCP is available:

1. Check editor state and console before acting.
2. After script edits, wait for compilation.
3. Read console errors/warnings.
4. If behavior is visual/scene-facing, capture a screenshot or inspect scene hierarchy.

Useful MCP actions:

```text
read_console
manage_scene get_active / get_hierarchy
find_gameobjects
validate_script
run_tests
manage_camera screenshot
```

When Unity MCP is not available:

- Use `rg` to inspect scripts and YAML.
- Be explicit in the final answer that runtime console/play verification was not possible.
- Avoid claiming Unity compiled or Play Mode passed unless actually verified.

## Search Tips

Use `rg`, not recursive grep:

```bash
rg "SearchTerm" Assets/Scripts Assets/Scenes Assets/Prefabs Assets/ScriptableObjects
rg --files Assets/Scripts -g '*.cs'
```

Ignore noisy folders:

```bash
rg --files -g '!Library/**' -g '!Temp/**' -g '!Logs/**' -g '!obj/**'
```

## Git Notes

The working tree may already be dirty. Do not revert changes you did not make. In particular, scene YAML, font assets, screenshots, and generated files may have user/editor changes.

Before summarizing work, check:

```bash
git status --short
git diff --stat
```

Keep final reports concrete:

- what changed
- why it solves the task
- what was verified
- what could not be verified
