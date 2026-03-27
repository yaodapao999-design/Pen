# 🎮 Unity 项目 Git 版本控制与开发规范

## 一、 Git 基础操作与分支管理

### 1. 分支命名与管理
为了保证项目的稳定性，请严格按照以下规范管理分支：
*   **`main` (主分支)**：永远保持可运行、无严重 Bug 的状态。
*   **`feature/` (功能分支)**：每开发一个新功能，都必须从 `main` 分支拉取一个新的 `feature/功能名` 分支。
*   **`fix/` (修复分支)**：用于修复 Bug 的专用分支。

### 2. 基本操作概念
*   **Commit (提交)**：将修改保存到本地。
*   **Push (推送/上载)**：将本地的 Commit 上传至云端，团队其他成员才能看到你的修改。先全部Commit所有修改再Push。
*   **Pull (拉取)**：从云端获取最新的代码和资源到本地。

### 3. 常见开发场景与代码暂存
*   **场景 A：在功能分支开发时发现 Bug**
    需要在当前功能分支中创建一个新的 `fix/` 分支。如果当前有未提交的修改，切换分支时选择 **Bring my changes**（带走修改）。修复完成后，将该 `fix` 分支合并（Merge）回原本的功能分支。
*   **场景 B：功能开发一半，需要紧急回到 main 分支添加其他内容**
    切换回 `main` 分支时，选择 **Leave my changes**（暂存/留下修改，不带入 main），然后再基于 `main` 创建新的分支进行开发。

### 4. 撤销与回退 (Rollback)
如果你提交了错误的内容，请根据以下情况处理：
*   **情况 1：已 Commit，但未 Push**
    在 GitHub Desktop 左侧历史记录中找到刚才那条提交，右键选择 **Undo** 即可撤销。
*   **情况 2：已经 Push 到云端**
    在历史记录中右键选择 **Revert Commit**，系统会自动生成一条新提交（内容是撤销那条错误的修改），然后再次 **Push origin** 同步到云端。

---

## 二、 美术资产 (Art Assets)
美术资源建议统一使用小写，单词间用下划线 `_` 分割，方便在代码中快速定位。

| 资产类型 | **推荐前缀** | **示例** | **备注** |
| :--- | :--- | :--- | :--- |
| **模型 (Mesh)** | `mod_` | `mod_player_hero.fbx` | 3D 原始模型文件 |
| **预制体 (Prefab)** | `pfb_` | `pfb_bullet_fire.prefab` | **最常用**，游戏中的实例 |
| **材质 (Material)** | `mat_` | `mat_wood_floor.mat` | 材质球 |
| **纹理 (Texture)** | `tex_` | `tex_hero_albedo.png` | 建议加后缀：`_n`(法线), `_m`(金属度) |
| **精灵 (Sprite)** | `spr_` | `spr_ui_icon_health.png` | 2D UI 或贴纸 |
| **动画 (Animation)** | `anim_` | `anim_player_idle.anim` | 单个动画剪辑 |
| **控制器 (Controller)** | `ac_` | `ac_player_main.controller` | 状态机 (Animator) |

---

### 🎵 音乐音效 (Audio Assets)
音频建议按照“用途”分类，这样在 GitHub 提交时，你能清晰区分是改了背景音乐还是补了一个小音效。

| 资产类型 | **推荐前缀** | **示例** | **备注** |
| :--- | :--- | :--- | :--- |
| **背景音乐 (BGM)** | `bgm_` | `bgm_level01_forest.mp3` | 较长、循环播放的音频 |
| **环境音 (Ambient)** | `amb_` | `amb_rain_loop.wav` | 风声、雨声等氛围音 |
| **音效 (SFX)** | `sfx_` | `sfx_player_jump.wav` | 短促的动作反馈音效 |
| **语音 (Voice)** | `voc_` | `voc_npc_guide_01.wav` | 角色台词或配音 |

---

## 三、 Unity 项目提交规范 (Commit Message)

### 1. 基本格式
提交信息应避免使用“Update”、“Fixed”这种模糊的词汇，请采用以下标准格式：
```text
<类型>(<模块>): <简短描述>

[可选：详细描述原因，尤其是修改了 Inspector 参数或 Prefab 结构时]
[可选：关联的 Issue 编号，如 Fixes #102]
```

### 2. 类型定义 (Type)

| 类型 | 适用场景 (Unity 语境) |
| :--- | :--- |
| **feat** | 开发新功能（如：新的技能系统、二段跳功能、新 UI 界面） |
| **fix** | 修复 Bug（如：修复碰撞体失效、修复 Boss 不掉血 Bug） |
| **asset / art** | **美术与音频资源更新**（如：更新主角行走动画、导入新模型、替换贴图） |
| **pref** | 预制体/场景修改（如：修改 Prefab 上的组件参数、调整场景布局） |
| **perf** | 性能优化（如：减少 DrawCall、优化 Update 逻辑、压缩贴图） |
| **refactor** | 代码重构（如：重构背包系统逻辑、解耦管理器类） |
| **docs** | 文档更新（如：更新设计文档、编写代码注释） |
| **chore** | 杂务（如：更新 Unity 版本、修改 ProjectSettings、更新插件） |

### 3. Unity 专属实例演示
*   **功能开发：** `feat(player): 实现玩家二段跳逻辑`
*   **资源更新：** `asset(boss): 导入 Boss 的攻击动画和受击音效`
*   **参数调整：** `pref(ui): 调整主菜单按钮的点击响应区域和颜色反馈`
*   **修复 Bug：** `fix(physics): 修复子弹穿过墙壁的检测问题`
*   **代码重构：** `refactor(inventory): 重构背包系统的逻辑代码`
*   **项目配置：** `chore: 将项目升级至 Unity 2022.3.x LTS 并更新 Addressables 插件`

### 4. 针对 Unity 的特别建议
*   **精准描述修改对象：** 尽量不要只写 `Update Player`。要写清楚是修改了脚本逻辑 (`Update Player Script`) 还是组件参数 (`Update Player Prefab`)。
*   **记录 Inspector 的变化：** 如果修改了 Inspector 面板里的关键参数，建议在详细描述 (Description) 里写明。例如：*“将 Boss 移速从 5.0 降至 3.5 以平衡难度”*。
*   **大文件处理：** 导入大型 4K 贴图或视频时，建议在 Description 中注明是否已设置了正确的导入压缩格式（Import Settings），防止协作者拉取后卡顿。

### 5. GitHub Desktop 快速操作技巧
在 GitHub Desktop 中提交时，合理利用标题和描述框：
*   **Summary (摘要):** 输入 `feat(weapon): 增加激光枪武器`
*   **Description (描述):** 输入 `调整了激光特效的渲染层级，解决了与水面着色器的显示冲突。`

---

## 四、 C# 代码编写规范

在 Unity 脚本编写中，请严格遵守以下命名规范：

```csharp
// 1. 类名：大驼峰命名法 (PascalCase)
public class PlayerMovement { }

// 2. 方法名：大驼峰命名法 (PascalCase)
public void CalculateDamage() { }

// 3. 变量名：小驼峰命名法 (camelCase)
private int maxHealth;

// 4. 常量：全大写 + 下划线 (UPPER_SNAKE_CASE)
public const float GRAVITY = 9.8f;
```