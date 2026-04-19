# Plan output（文档驱动，Unity）

Plan 模式的产出必须写入 `Assets/Plans/` 下的 Markdown（除非用户明确要求只留在聊天里）。

## 目录与命名

- 一 GDD 一目录：`Assets/Plans/<GDD名>/`；目录名与 GDD 同名同语义。未定名时先给 1–3 个候选让用户选。
- 多份实现计划：`Assets/Plans/<GDD名>/implementation/<purpose>.md`（可多文件）。文件名用用途（建议 kebab-case），不用日期当标题。

## 文档层级（自上而下）

1. **GDD**：`game-design.md`
2. **实现计划**：`implementation/<purpose>.md`
3. **实际代码**：仓库内真实 `.cs`（非计划 md；在实现计划中用专门章节对齐）
4. **配置指南**：`editor-setup.md`（以脚本实际暴露的 Inspector/配置为准）

## `game-design.md`

- 含「实现计划」小节，用**相对路径**链接到该 GDD 下**全部** `implementation/*.md`。

## `implementation/<purpose>.md`（章节顺序固定）

- `## 背景` / `## 目标` / `## 非目标` / `## 方案概述`
- `## 可编码的代码结构`：命名空间/类型职责、public API、组件与数据流、拟议 `Assets/...` 文件树等，细化到可直接开写代码。
- `## 实际代码层（CS 脚本）`：每个将新增或修改的 `.cs` 的**仓库路径**、**类型/命名空间**、**Inspector 可见字段清单**（名称、类型、默认值、范围/单位、影响、推荐值）、对外 API/事件与依赖。
- `## 变更清单（按文件路径）` / `## 风险与回滚` / `## 测试计划`
- `## Checklist`：`- [ ]` 勾选清单，至少含：按上节每个 `.cs` 的落地项；Unity 集成（场景/Prefab/资源等；若需读 `.unity/.prefab` 须含「用户已授权或已点名路径」）；与测试计划对齐的验收项；`game-design.md` / `editor-setup.md` / 本文件同步（参数变时先改实现计划再改配置指南）。

可选：`implementation/tasks.md`、`implementation/test-plan.md`。

## `editor-setup.md`（章节顺序固定）

`## 适用范围` → `## 配置入口` → `## 依赖与前置条件` → `## 配置步骤（可复现）` → `## 参数说明（所有可见参数）` → `## 常见错误与排查`。

「所有可见参数」须逐项：名称、类型、默认值（若知）、范围/单位、对玩法或性能影响、推荐值；并与对应实现计划中 `## 实际代码层（CS 脚本）` 的可见字段清单一致；实现变动时先更新实现计划再更新本文件。

## Unity 与场景资源

- 变更清单写到常见路径（如 `Assets/Scripts/`、`Assets/Prefabs/`、`Assets/Scenes/`、`ProjectSettings/`），并注明是否需查看 `.unity/.prefab`。
- 未获用户明确授权前，默认不读 `.unity/.prefab`（见 `unity-context.md`）。

## 更改日志与维护

- `game-design.md`、`editor-setup.md`、所有 `implementation/*.md` 文末须有 `## 更改日志`（日期或版本/里程碑、摘要；作者可选）。
- 目标文件不存在则创建；已存在则增量更新，避免无故改名或拆文件。
