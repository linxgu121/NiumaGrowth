# NiumaGrowth

## 模块定位
NiumaGrowth 是成长/技艺熟练度模块，负责技能或技艺经验、等级计算、继承事实、成长条件、存档和成长 UI 数据。

## 框架设计思路
- 存档保存 TotalExp，不保存 CurrentLevel，等级由当前配置阈值实时计算。
- 阈值调整后旧存档不会错位，只会按新配置重新计算等级。
- GrowthRequirementData 给 Skill、Quest、Story 等模块判断是否满足成长条件。
- 技能释放成功、任务奖励、剧情传承都可以显式 AddExp。

## 核心流程
1. GrowthController 加载 GrowthSkillDefinition。
2. AddExp / SetExp 修改 ActorId + SkillId 的 TotalExp。
3. Service 根据 LevelThresholds 计算等级、当前级经验、下一级需求。
4. LevelChanged / ExpChanged 可通过 EventBus 通知表现层。
5. SaveAdapter 保存各 Actor 的 TotalExp。
6. UI Bridge 输出技艺列表、等级、进度条和满级状态。

## 模块用法
- SkillId 在 Growth 中表示“成长项 ID”，可以与 NiumaSkill 的 SkillId 对齐。
- LevelThresholds 必须升序且非负，配置错误的定义不应进入可用表。
- 0 经验默认不导出，表示与“从未接触”合并处理。

## 场景使用方法
推荐放置方式：`GrowthRoot` 一个成长根物体承载成长服务、UI 桥接和存档。

- `GrowthRoot`：挂 `NiumaGrowthController`，绑定 GrowthSkillDefinition 列表。
- `GrowthRoot/SaveAdapter` 或全局 `SaveRoot/Providers`：挂 `NiumaGrowthSaveAdapter`。
- `GrowthRoot/UIBridge` 或 `UIRoot/Bridges`：挂 `GrowthUIViewBridge`，绑定成长面板 Receiver。
- `GrowthRoot/Debug`：开发阶段挂 `GrowthBasicTestRunner`。
- `SkillRoot`：技能释放成功后调用 GrowthCommand.AddExp。
- `QuestRoot` 或 `StoryRoot`：任务奖励、剧情传承可调用 AddExp 或 ApplyInheritance。
- `UIRoot/GrowthPanel`：放技艺列表、等级、经验条、满级状态和分类筛选。
- 多角色场景仍只需要一个 GrowthController，通过 ActorId 区分玩家、NPC 或联机玩家。

## 协作边界
Growth 不释放技能、不处理冷却、不计算伤害。Skill 只在释放成功后调用 GrowthCommand 增加经验。

## 场景挂载与 Inspector 配置
### NiumaGrowthController
建议挂载位置：`CoreScene/BootstrapRoot/GameplayServicesRoot/GrowthRoot`。

用途：管理技艺熟练度、总经验、等级计算和继承事实。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Growth Skill Definitions` | 拖所有技艺定义资产 | 不建议 | 技艺 ID 会 MissingDefinition，无法显示名称和等级阈值 |
| `Register Service To Context` | 核心场景开启 | 可以关闭 | Skill、Reward 等模块无法从 GameContext 增加经验 |
| `Log Warnings` | 建议开启 | 可以 | 配置错误不提示 |

### NiumaGrowthSaveAdapter
建议挂载位置：`CoreScene/BootstrapRoot/SaveRoot/SaveAdapters`。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Growth Controller` | 拖 `NiumaGrowthController` | 不建议 | 技艺经验不存档 |
| `Save Controller` | 拖 `NiumaSaveController` | 不建议 | 无法注册存档 Provider |

### GrowthUIViewBridge
建议挂载位置：技艺面板 UI 物体。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Growth Controller` | 拖 `NiumaGrowthController` | 不建议 | UI 不刷新 |
| `Actor Id` | 填角色 ID，例如 `player` | 不可以 | 不知道显示谁的熟练度 |
| `Receiver Provider` | 拖技艺面板 UI 接收脚本 | 不可以 | ViewData 无处显示 |


