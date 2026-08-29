# HANDOFF — TFC 离线群系查看器 .NET 移植（2026-08-29 晚）

## 一句话状态
**C# 与 Java 基准 512×512 全图（testseed2=-1162099057 和 12345）像素级 100% 一致（0/262144 diff）**，
含河流。剩余任务：WinForms 查看器 + self-contained 打包（纯交付物工作，无引擎差异）。

## 2026-08-29 修复的三个真实根因（全部在 C# 侧）

### 1. findBestStartingAngle 用错常量（河流数量差异 622 vs 562 的真根因）
- Java `Float.MIN_VALUE` = **最小正数** 1.4e-45；C# `float.MinValue` = **最负数** -3.4e38
- 后果：Java 只接受 "dO - |dx| - |dz| > 1.4e-45" 的方向（河流起点必须深入内陆≥2-3格），
  C# 接受任何 land 方向 → shore 有效起点 730 vs 428 → 每个 builder 的随机种子序列整个错开
  → AddRiversAndLakes 后 region.random 状态分歧 → ChooseBiomes 的 rngSeed 错位 → **biome 全乱**
- 修复：RegionTasks.cs `float bestDistanceMetric = float.Epsilon;`（= Java Float.MIN_VALUE）
- 验证：修复后 river=562、probeNextLong=-689588871835387608 与 Java 完全一致

### 2. TFCLayers.ShoreFor 残缺版（层链海岸差异 3.53% 的真根因）
- C# 版只实现了 Java shoreFor 的一部分分支，缺：OLD_MOUNTAINS/EXTREME_DOLINE_MOUNTAINS→TERRACE_LOWER、
  PLATEAU→SEA_STACKS、PLATEAU_WIDE→SETBACK_CLIFFS、HIGHLANDS→ROCKY_SHORES、ROLLING_HILLS→EMBAYMENTS、
  HILLS→COASTAL_DUNES、VOLCANIC_ISLAND→SHORE；fallback 也错（C# return SHORE，Java 是 TIDAL_FLATS）
- 后果：层链 ShoreAndRiverLayer 输出错误 → 海岸带 3.53% 像素不同（J=39/37/32 vs C=31 全是 SHORE）
- 修复：LayerCore2.cs ShoreFor 补全为 Java 完整版（与 TFCLayers.java shoreFor 逐行一致）

### 3. 渲染输出 Y 翻转 + 脏写入（图像 73.89% diff 的掩盖因素）
- Program.cs 的 flipped 翻转方向反了：image[iz]（iz=0=北）被翻到 PNG 底部 → 输出南北颠倒
- `image[size - 1 - iz * 1 + iz * 0] = 0;` 不是 no-op，是把 image[0..511] 置 0 → 511 个黑像素
- 修复：删除 flipped 直接输出 image；删除脏写入行
- 注意：512×512 与 Java 对拍时 raw 直接对比（顶部都=北）；早期 32×32 "翻转后再对比仍 99% diff"
  是因为真正原因是河流+层链，翻转只是额外项

### 附带修正（渲染路径补齐，Java 早有 C# 缺）
- Program.cs 取数：`bx >> 2` → `Mth.FloorDiv(bx,16)*4+1`（chunk 中心 quart，Java Main 同款）
- Program.cs 新增 GetBiomeAt() = 等价 BiomeSourceExtension.getBiomeExtension：
  层链 → HasRivers(biomeId) → GetOrCreatePartitionPoint(BlockToGrid(quartX<<2),…) →
  遍历 edge.Fractal.Intersect(exactGridX, exactGridZ, 0.08f) → RIVER
  （此前 C# 渲染完全没有河流检测，也没有 partition 查询 → region 只创建 1 个 vs Java 16 个）
- TFCLayers2.cs 新增 HasRivers(layerId)：noRivers 名单 10 个（SHORE/TIDAL_FLATS/SEA_STACKS/TERRACE_UPPER/
  TERRACE_LOWER/SETBACK_CLIFFS/COASTAL_DUNES/ROCKY_SHORES/EMBAYMENTS/MELTWATER_LAKE，自 TFCBiomes.java 提取）

## 排查方法论（下次用）
1. **dump 对拍必须全覆盖 region 内所有点**（不是查询点）：本次 15251 点全量 dump 才发现 shore 集合
   实际一致，河流差异根因在 findBestStartingAngle 的常量
2. **Java noRiver 模式本身是坏的**（getPartition → region.rivers() null NPE），不是有效基准；
   "Java noRiver 9 个 INIT" 只是崩溃前的计数。**只对拍 river 模式**
3. 河流随机状态对拍：AddRiversAndLakes 后的 probeNextLong 是致命探针（Java 日志已有）
4. 层链二分定位：dump 加中间层列（zoom1/shore/more/ice/z2..z5/smooth）一次跑出第一分歧层
5. 渲染对拍时先确认坐标系（顶部=北）与脏写入，避免被 73% 假 diff 带偏 1 小时

## 验证结论（river 模式，最新）
- 512×512 testseed2：0/262144 diff（100%）
- 512×512 seed 12345：0/262144 diff（100%）
- 河流：562 = Java 一致；AddRiversAndLakes 后 probe = -689588871835387608 一致
- biome/p.biome：15251 点全覆盖 0 diff（当时）；river/lake flag 0 diff
- 层链全层（zoom1/shore/more/ice/z2/z3/z4/z5/smooth）：4096 点抽样 0 diff
  （注：修复 ShoreFor 后未重跑全量 dump，但 512 图 0 diff 已充分证明）

## 项目位置
- **C# 工程**: `/opt/data/home/workspace/minecraft/research/tfcviewer-cs/`（结构见下）
  - src/Program.cs（CLI：取数=chunk 中心 quart + GetBiomeAt 河流检测；输出顶部=北）
  - src/RegionTasks.cs（AddRiversAndLakes：float.Epsilon 修复；ChooseBiomes 等）
  - src/LayerCore2.cs（ShoreFor 已补全；HasRivers）
  - src/DumpCompare.cs（已扩展：dump 含层链中间列 zoom1..smooth、divergence、areaSeed——可继续用于对拍）
- **Java 基准工程**: `/opt/data/home/workspace/minecraft/research/tfcviewer/`
  （DumpCompare.java 也已加中间层列+divergence+areaSeed；AddRiversAndLakes 无插桩）

## 构建/运行（容器）
```bash
cd /opt/data/home/workspace/minecraft/research/tfcviewer-cs
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 /opt/dotnet8/dotnet build -c Release
DOTNET_ROOT=/opt/dotnet8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./bin/Release/net8.0/tfcviewer testseed2 512 16 biome /tmp/out.png
```
Java 基准：`cd research/tfcviewer && javac --release 21 -cp build/lib/fastutil-8.5.15.jar -d build/out $(find src/main/java -name '*.java')`
（注意 build.sh 的 EXIT=0 是假信号：javac 输出进了管道，出错时 EXIT 仍 0；直接看输出有没有 error）
对拍 raw：PPM/PNG → ffmpeg -f rawvideo -pix_fmt rgb24，python 逐像素。

## 剩余任务
1. **WinForms 查看器**（第一批交付物）：主画布 + 小地图 + 拖动/滚轮/图层切换（参考 Java ViewerMain）
2. **打包**（第二批）：self-contained win-x64 exe（`dotnet publish -r win-x64 --self-contained`），
   图标 assets/wheat.png，使用说明 txt
3. 完成交付物 + 更新 research/README.md 结论

## 注意
- Windows 上无需 Java；self-contained 无 .NET 依赖
- 用户环境：NeoForge 21.1.248 游戏存档 seed testseed2=-1162099057（Java hashCode）
- DLL 依赖：C# 零外部包（PNG 自写编码器），不要引入 System.Drawing
- 调试开关保留：TFC_TASK_LOG / TFC_DEBUG_RIVER / TFC_NO_RIVER / TFC_SEED_LOG（原本就有）