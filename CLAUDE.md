# Pen — Claude Code 项目指引

本仓库是一个 Unity 项目,采用**文档驱动开发(DDD)**工作流。方法论总览见 [Assets/Plans/Document-Driven-Development.md](Assets/Plans/Document-Driven-Development.md)。

## 硬规则 1:Plan 产出必须落盘到 Assets/Plans

所有 Plan 模式产出、实现计划、GDD 相关文档**必须**写入 `Assets/Plans/<GDD名>/` 下的 Markdown 文件,除非用户明确说"只留在聊天里"。

- 本项目内创建 `Assets/Plans/**/*.md` **不受**"不主动创建 md 文件"默认规则限制——这是用户授权的项目规则
- ExitPlanMode 涉及非平凡实现时,须同步产出/更新 `Assets/Plans/<GDD名>/implementation/<purpose>.md`,不要把计划只留在 plan 文本里
- 用 Plan 子代理(`subagent_type=Plan`)时,在 prompt 中明确要求按下文章节顺序产出并写入正确路径

### 目录与命名

- 一 GDD 一目录:`Assets/Plans/<GDD名>/`
- 未定 GDD 名时先给 1–3 个候选让用户选
- 多份实现计划:`Assets/Plans/<GDD名>/implementation/<purpose>.md`(kebab-case,不以日期命名)

### 四层文档

1. **GDD**:`game-design.md` —— 做什么/为什么/边界;含「实现计划」小节,用**相对路径**链接到该 GDD 下全部 `implementation/*.md`
2. **实现计划**:`implementation/<purpose>.md` —— 怎么做
3. **真实代码**:仓库中真实的 `.cs` 文件(不是 md)
4. **配置指南**:`editor-setup.md` —— Inspector 参数说明;以脚本实际暴露字段为准

### 实现计划章节顺序(固定)

`## 背景` → `## 目标` → `## 非目标` → `## 方案概述` → `## 可编码的代码结构` → `## 实际代码层(CS 脚本)` → `## 变更清单(按文件路径)` → `## 风险与回滚` → `## 测试计划` → `## Checklist`

- **`## 可编码的代码结构`** 须细化到能直接开写代码:命名空间、类型职责、public API、组件与数据流、拟议 `Assets/...` 文件树
- **`## 实际代码层(CS 脚本)`** 是脚本契约:每个新增/修改 `.cs` 的仓库路径、类型/命名空间、**Inspector 可见字段清单**(名称、类型、默认值、范围/单位、影响、推荐值)、对外 API/事件、依赖
- **`## Checklist`** 用 `- [ ]` 勾选项,至少含:按上节每个 `.cs` 的落地项;Unity 集成(场景/Prefab/资源;涉及读 `.unity/.prefab` 的条目须含"用户已授权或已点名路径"字样);与测试计划对齐的验收项;`game-design.md` / `editor-setup.md` / 本文件同步(**参数变动时先改实现计划再改配置指南**)
- 可选扩展:`implementation/tasks.md`、`implementation/test-plan.md`

### 配置指南章节顺序(固定)

`## 适用范围` → `## 配置入口` → `## 依赖与前置条件` → `## 配置步骤(可复现)` → `## 参数说明(所有可见参数)` → `## 常见错误与排查`

「所有可见参数」须逐项:名称、类型、默认值、范围/单位、对玩法或性能影响、推荐值;**必须与对应实现计划 `## 实际代码层` 的字段清单一致**。实现变动时**先更新实现计划,再更新本文件**。

### 更改日志与维护

- `game-design.md`、`editor-setup.md`、所有 `implementation/*.md` 文末须有 `## 更改日志`(日期或版本/里程碑 + 摘要,作者可选)
- 目标文件不存在则创建;已存在则增量更新,**避免无故改名或拆文件**

## 硬规则 2:默认不读 Unity 序列化文件

**默认不主动** Read `.unity`(场景)、`.prefab`(预制体),也不通过 Grep/Glob 把其内容拉进上下文。原因:这些文件是 YAML 序列化,内容庞杂、可读性差,会污染上下文。用户希望明确区分"代码逻辑问题"与"场景/预制体配置问题",前者绝大多数不需要读序列化文件。

**允许读取的情形(满足任一即可)**:
1. 用户在消息中明确说"查看/读取场景文件/预制体"等字样
2. 用户点名具体路径,如 `Assets/.../*.unity` 或 `Assets/.../*.prefab`
3. 用户此前在当前会话已授权,且后续操作属于同一范围的延续

**未授权但判断必须读取时**:**先停下来询问**,说明原因与将要读取的文件清单,等用户同意后再读。

**其他要点**:
- `.meta` 文件也不需读,除非用户明确要求排查 GUID / 引用问题
- Grep/Glob 返回 `.unity/.prefab` 路径(仅文件名,不含内容)是允许的,但不要顺手对它们 Read
- 使用 Agent(如 Explore)探索代码库时,须在 prompt 中指明"不读取 `.unity/.prefab` 内容,除非我之后点名"
- 调试任务若怀疑是场景/预制体配置问题:先向用户**汇报结论并请求授权**,再读序列化文件

## 自动化:Plans 同步检查 Hook

[.claude/settings.json](.claude/settings.json) 注册了 `PostToolUse(Write|Edit|MultiEdit)` hook,调用 [.claude/hooks/plan_docs_sync.py](.claude/hooks/plan_docs_sync.py)。当编辑的是 `Assets/Plans/<GDD>/` 下的 `.md` 时,会扫描并通过 `additionalContext` 回注问题:

- `game-design.md` 中的相对 md 链接断链(error)
- `game-design.md` 缺少指向 `implementation/*.md` 的链接(warn)
- 本次 Edit 的 `old→new` 替换在兄弟文档中的残留(warn)
- `Assets/**/*.cs` 在 `implementation/*.md` 与 `editor-setup.md` 之间的覆盖一致性(warn)

Fail-open:异常时返回空 JSON,不阻塞工具。

## 参考

- [Assets/Plans/Document-Driven-Development.md](Assets/Plans/Document-Driven-Development.md) — DDD 方法论
- [Assets/Plans/Project-Overview.md](Assets/Plans/Project-Overview.md) — 项目概览
- [Assets/Plans/Coding-Guide.md](Assets/Plans/Coding-Guide.md) — 编码规范
- `.cursor/rules/plan-output.md`、`.cursor/rules/unity-context.md` — 原始 Cursor 规则(与本文对齐)
