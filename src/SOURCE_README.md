# 多洛可日常信息增强 —— 源码与构建说明

这是「多洛可日常信息增强」v1.9.8 的完整源码（C#，.NET Standard 2.1）。
你不需要接受任何许可协议：按原模组的说明，只要**标明原模组**就可以随意修改、上传改良版。

## 目录

```
source/
├─ SomeOptimizations.csproj   工程文件（会自动查找游戏目录）
├─ game.path.example          把 game.path.example 改名成 game.path，写一行游戏路径
├─ heart.png                  内嵌资源（生日提醒的小爱心）
├─ SOURCE_README.md           本文件
├─ ModEntry.cs                入口：Harmony 装载、事件订阅
├─ ModConfig.cs / Settings.cs DTMAPI 配置项
├─ Layout.cs                  可调版面参数 + 逐作物偏移
├─ ModState.cs                state.json 读写（礼物/对话记录、逐作物偏移）
├─ CropScan.cs                农场扫描与成熟时间预测（核心算法，含立木架）
├─ Festivals.cs               节日日期区间（长夜节/蘑菇节）与提醒时段判断
├─ FilmScan.cs                塑料薄膜剩余时间扫描（按农场）
├─ GameTime.cs                游戏时间/取整工具
├─ Reminders.cs               徽章构建：派克 / 生日 / 游商 / 作物 / 节日
├─ HudBadges.cs               时钟下方的悬挂图标行
├─ CalendarInject.cs          日历格子的作物图标 + 右侧特殊事件行
├─ PreviewUi.cs               日历微调的预览面板（自绘 7x4 网格）
├─ EditorUi.cs                布局编辑器（默认 F7 打开、Tab 进预览）
├─ UiFactory.cs               程序化贴图 / 图标裁边（IconFit）
├─ UiRoot.cs                  UI 宿主 Canvas
├─ Tracking.cs                送礼 / 对话的当日记录
├─ ResinPriority.cs           树脂收集器互动优先级
├─ CropDefaults.cs            逐作物微调的内置默认值
└─ Diagnostics.cs             调试日志（默认关闭）
```

## 构建

要求：Windows + .NET SDK 8（`dotnet --version` 能看到 8.x）。
游戏里需要先装 **DTMAPI**（它的 `DTMAPI.Abstractions.dll` 是编译依赖）。

1. 复制 `game.path.example` 为 `game.path`，把里面那一行改成你的游戏目录，例如：
   `D:\SteamLibrary\steamapps\common\Doloc Town`
   （也可以不改文件，改用命令行：`dotnet build -c Release -p:GameDir="D:\...\Doloc Town"`）
2. 在本目录执行：
   ```
   dotnet build -c Release
   ```
3. 产物：`bin\Release\SomeOptimizations.dll`

## 安装（本地测试）

把 DLL 覆盖到：

```
%LocalAppData%Low\RedSawGames\DolocTown\MODS\SomeOptimizations\Content\DTMAPI\SomeOptimizations.dll
```

然后在游戏里「模组」列表启用，重启游戏。
调试日志在 `游戏目录\BepInEx\LogOutput.log`，搜 `[SomeOptimizations]`。

## 代码导读（想改哪里看哪）

| 想改的东西 | 文件 | 关键点 |
| --- | --- | --- |
| 成熟时间怎么算 | `CropScan.cs` | `TryPredict()`：`rate = crop.LastGrowth`（优先，含肥料/补光灯/基因）→ 否则基因+肥料理论值；逐级 `ceil(left / rate)`，每级溢出会被游戏丢弃 |
| 立木架（树）怎么算 | `CropScan.cs` | `TryPredictTree()`：读 `TreeCrop.maxGrowthValues`（每棵树随机生成），逐级 `ceil(left / rate)`，`rate` 固定 1（+肥料）；**不要调用 `TreeCrop.Growth`**，它会扣肥料计时 |
| 成熟日期落在哪一天 | `CropScan.cs` | `TryBucket()`：先取整到整分钟再分桶；正好 24:00 算当天（显示 `24:00`）；落在过去的样本并入当天，不丢弃 |
| 节日日期与时段 | `Festivals.cs` | 从 `TbCalendar` 里按事件 id 前缀（`festival_evernight` / `festival_mushroom`）反推区间，不写死日期 |
| 批次怎么合并 | `CropScan.cs` | `ModConfig.SameBatchMinutes`（默认 60 分钟）内算同批 |
| 日历格子图标 | `CalendarInject.cs` | `PlaceCropIcon()`：先按可见像素对齐（`IconFit`），再夹进格子，再加逐作物偏移 |
| 右侧事件行 | `CalendarInject.cs` | `AppendRows()` + `RowsThatFit()`（按实时高度算能放几行） |
| 时钟下方徽章 | `HudBadges.cs` | 一个 `Slot` = 背景板 + 图标 + `+N` + 爱心 + 描边高光 |
| 谁该出现 | `Reminders.cs` | `AddPike/AddBirthdays/AddMerchants/AddCrops/AddFilm/AddFestivals` |
| 薄膜剩余多久 | `FilmScan.cs` | `HoursLeft()`：点数 × `TU2Min` / 60；**不要用 `ProtectedPercent`**（240 满格是显示 bug，新膜是 576 点） |
| 位置/大小 | `Layout.cs` | 所有可调项都在 `Items` 表里，键名与 DTMAPI 菜单一一对应 |
| 预览面板 | `PreviewUi.cs` | 自绘 7×4 网格，**已知问题**：背景不是日历贴图、`+N` 位置与正式日历略有差异 |
| 树脂收集器 | `ResinPriority.cs` | 后置 `Equipment.Interactable` getter，空置时把 `Priority` 降到 -1 |

## 已知可改进点（原作者列出的）

1. 预览面板里植物的背景 UI 做成和日历一致、`+N` 回到正常位置（v1.6 时是对的）。
2. 编辑器界面美化。
3. 编辑器改用鼠标操作，而不是键盘。
4. 性能占用可能偏高（未确认）：`CropScan` 目前每秒重建一次全部农场的快照。
5. 简介/文案优化。

## 注意

- 这个 DLL 会在所有玩家侧执行，**不要在里面加任何上传/联网/文件删除逻辑**。
- 改 `UniqueID`（`SomeOptimizations`）会让存档认为换了一个模组，别改。
- 卸载模组不会破坏存档：本模组只写 `DTMAPI\config\SomeOptimizations.json` 和 `SomeOptimizations.state.json`，不碰存档数据。
