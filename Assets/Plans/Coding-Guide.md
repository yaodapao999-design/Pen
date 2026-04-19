# Unity 独立游戏编码指南（基于 12 条软件工程原则）

本文整理自 `12 Core Software Development Principles Every Software Engineer Must Know`（来源页面见 PDF 页眉/页脚链接），并按 **Unity 独立游戏**常见约束做落地化改写：小团队、迭代快、Prefab/场景/资源耦合强、性能敏感、可维护性主要靠自觉与文档。

> 这些原则是“导向”，不是教条；在独立游戏中，**可交付**与**可维护**之间的权衡要写在实现计划里（见 `Assets/Plans/Document-Driven-Development.md` 与 `/.cursor/rules/plan-output.md`）。

## 12 条原则 → Unity 实践要点

### 1) DRY（不要重复自己）

- **规则数据、数值曲线、关卡参数**：优先 `ScriptableObject` / 配置资产，而不是散落在多个 Prefab/多个脚本常量里。
- **字符串与路径**：用 `const`/`static readonly`、枚举、或集中常量类；避免 `"Player"`、`"Enemy"` 魔法字符串满天飞。
- **重复逻辑**：抽到纯 C# 服务/工具类（无 Unity API 依赖时更易测），或抽到可复用组件；不要为了“复用”制造过度抽象。

### 2) KISS（保持简单）

- **先跑通闭环**：战斗/移动/存档等核心循环用最短路径实现，再迭代打磨。
- **避免“框架先行”**：没有第二个真实用例前，不要写大而全的事件总线/ECS/插件化架构。
- **Inspector 暴露字段**：只暴露真正需要调参的字段；其余用 `[SerializeField]` 谨慎分级（必要时用 `[HideInInspector]` 或自定义 Editor）。

### 3) YAGNI（你不会需要它）

- **不做预支特性**：多武器槽、多存档云同步、全量本地化框架等，除非当前里程碑真需要。
- **避免“为未来接口预留”的大量抽象层**：独立游戏更需要快速验证玩法。

### 4) SOLID（面向对象设计的五原则）

- **SRP（单一职责）**：一个 MonoBehaviour 只做一类事（输入、动画驱动、战斗结算、UI 绑定分开）。巨型 `PlayerController` 是独立游戏最大技术债之一。
- **OCP（对扩展开放）**：用组合与数据驱动（SO、配置表）扩展行为，而不是到处改 `switch`/`if`。
- **LSP（可替换性）**：对“可替换单位/武器/状态”保持接口一致；避免子类偷偷改变父类契约（例如 `Update` 里隐含顺序依赖）。
- **ISP（接口隔离）**：不要把“巨大接口”塞给不需要它的客户端；Unity 里常见的是把 `IInteractable` 拆成最小能力集合。
- **DIP（依赖倒置）**：核心逻辑依赖抽象（接口/纯数据），具体 Unity 组件实现接口；便于替换与测试（但别过度 DI）。

### 5) Separation of Concerns（关注点分离）

- **分层建议（可按项目规模裁剪）**：
  - **表现层**：动画、音效、粒子、UI。
  - **玩法层**：规则、状态机、数值结算。
  - **基础设施层**：存档、输入、音频管理、对象池。
- **避免 UI 直接改玩法核心状态**：用明确的消息/命令/事件（哪怕是轻量 `Action`/`UnityEvent` 也要边界清晰）。

### 6) High Cohesion, Low Coupling（高内聚、低耦合）

- **内聚**：同一脚本内的字段与方法应围绕同一概念（例如 `Health` 就不要兼管背包）。
- **耦合**：优先数据引用（SO、配置）与显式依赖（构造函数/Init 方法/`[RequireComponent]`），少用全局单例；若用单例，限制数量并明确生命周期。

### 7) Fail Fast（尽快失败）

- **开发期断言**：关键不变量用 `Debug.Assert` 或自定义校验（例如状态机非法迁移立刻报错）。
- **对外部资源（Addressables/Resources/JSON）**：加载失败要**明确报错信息**（包含 key/路径），不要默默 `return`。
- **数据校验**：SO 用 `OnValidate` 做基础校验（范围、引用缺失），但不要写重量级逻辑。

### 8) Design for Testability（为可测试而设计）

- **把纯逻辑放在不依赖 `MonoBehaviour` 的类里**：时间、随机、输入可注入（接口/委托），便于单元测试。
- **避免在逻辑层直接调用 `UnityEngine.Object` 静态 API**：需要时钟就抽象 `IClock`，需要随机就抽象 `IRng`。
- **场景依赖**：核心系统用显式初始化顺序（Bootstrap），减少 `Awake/Start` 隐式顺序赌运气。

### 9) Encapsulation（封装）

- **字段默认 `private` + `[SerializeField]`**：公共 API 用方法表达意图（`TakeDamage`）而不是到处改 `public int hp`。
- **序列化边界**：搞清楚 `[SerializeField]`、`public` 字段、以及哪些数据应该进 SO/存档，而不是“能序列化就公开”。

### 10) Continuous Refactoring（持续重构）

- **小步重构**：每个里程碑留 10%～20% 时间还债；优先删死代码、拆大类、补边界。
- **重构触发器**：复制粘贴第三次就该抽；一个类超过 ~300 行且职责混杂就该拆（阈值按团队共识）。

### 11) Meaningful Names（有意义命名）

- **Unity 特有命名**：`Handle*`, `Try*`, `On*`, `*Config`, `*Settings`, `*Runtime` 等语义要一致。
- **序列化字段**：Inspector 里可读性很重要，必要时用 `[FormerlySerializedAs]` 处理改名迁移。

### 12) POLA（最小意外）

- **API 行为可预测**：`Init`/`Reset`/`Enable`/`Dispose` 的语义固定；不要在 `OnEnable` 里做一半初始化、`Start` 又做另一半让人猜顺序。
- **事件与回调**：订阅/反订阅对称；避免隐式全局副作用。

## Unity 独立游戏“加码”条款（强烈建议）

- **生命周期纪律**：明确 `Awake`（构建依赖）、`OnEnable`（订阅）、`Start`（跨对象初始化）、`OnDisable`（反订阅）各自放什么。
- **性能默认项**：Update 里少分配（GC）、少 LINQ、少字符串拼接；需要每帧做的事考虑事件驱动或缓存。
- **资源与场景**：加载策略（Addressables/场景分割）与卸载策略要写清楚；避免资源泄漏与重复加载。
- **输入与重绑**：输入层与玩法层分离；支持改键/手柄时不要在玩法里硬编码设备。
- **存档**：版本号、迁移、失败回滚；不要“能存就算成功”。

## 与文档驱动开发的衔接

- **先写清契约再写代码**：Inspector 暴露字段、关键 API、状态机迁移，应先在 `Assets/Plans/<GDD名>/implementation/*.md` 对齐，再落到 `.cs`（见 `Document-Driven-Development.md`）。
- **配置与实现对齐**：`editor-setup.md` 的参数说明必须覆盖所有可见字段，并与实现计划中的脚本字段清单一致。

## 参考

- PDF 原文页面（见导出 PDF 页眉/页脚 URL，例如 `https://coderower.com/blogs/software-development-principles-software-engineering`）。
- `Assets/Plans/Document-Driven-Development.md`
- `/.cursor/rules/plan-output.md`
- `/.cursor/rules/unity-context.md`
