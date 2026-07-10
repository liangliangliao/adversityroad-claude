# 接入 Mixamo 动捕角色 + 动作（真人级战斗动作）

代码已全部写好。你**只需要把下载好的文件放进指定文件夹**，Unity 会自动把它们配成
Humanoid、自动给走/跑/待机加循环——**无需在 Inspector 手动设置任何东西**。
运行时代码会自动加载它们；找不到就回退到原来的方块角色（所以放之前也不影响运行）。

---

## 一、放文件（就两步）

在工程里建好这个目录结构（没有就新建）：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.fbx          ← 玩家角色模型
├── EnemyModel.fbx           ← 敌人角色模型（可选，不放就复用 PlayerModel）
└── Anims/                    ← 所有动作 FBX 全部丢这里（不用改名）
    ├── Maria WProp J J Ong@Idle.fbx
    ├── Maria WProp J J Ong@Fighting Idle.fbx
    ├── Maria WProp J J Ong@Walking.fbx
    ├── Maria WProp J J Ong@Running.fbx
    ├── Maria WProp J J Ong@Great Sword Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Slash (1).fbx
    ├── Maria WProp J J Ong@Great Sword High Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Jump Attack.fbx
    ├── Maria WProp J J Ong@Stabbing.fbx
    ├── Maria WProp J J Ong@Lead Jab.fbx
    ├── Maria WProp J J Ong@Cross Punch.fbx
    ├── Maria WProp J J Ong@Kicking.fbx
    ├── Maria WProp J J Ong@Side Kick.fbx
    ├── Maria WProp J J Ong@Spin Flip Kick.fbx
    ├── Maria WProp J J Ong@Flying Kick.fbx
    ├── Maria WProp J J Ong@Hit Reaction.fbx
    ├── Maria WProp J J Ong@Knocked Down.fbx
    ├── Maria WProp J J Ong@Dying.fbx
    └── Maria WProp J J Ong@Spell Casting.fbx
```

**只需两处操作：**
1. 把角色模型 `Maria WProp J J Ong.fbx` 复制进 `Resources/Characters/`，**改名为 `PlayerModel.fbx`**。
2. （可选）把 `Paladin WProp J Nordstrom.fbx` 复制进去，**改名为 `EnemyModel.fbx`**。
3. 其余**所有动作 FBX 原样丢进 `Resources/Characters/Anims/`，不用改名**
   （代码按 Mixamo 的 `@动作名` 自动识别并分配到招式）。

> 动作文件名里的 `@Idle` / `@Side Kick` / `@Great Sword Slash` 这些**后缀**是关键，
> Unity 会用它给动画片段命名，代码据此匹配。别去掉 `@` 后缀就行。

---

## 一·补、动作库覆盖面补充清单（去 Mixamo 搜索下载，放进 `Anims/` 即自动生效）

以下动作当前用替代片段顶替，下载专用片段后代码**自动优先使用专用片段**（不用改任何配置，
文件名保留 `角色名@动作名.fbx` 的 `@后缀` 即可）：

| 去 Mixamo 搜索的动作名 | 生效位置 | 现在的替代方案 |
| --- | --- | --- |
| **Great Sword Blocking**（或 Blocking） | 格挡架势（挡键） | 格斗架势收紧 |
| **Stand To Roll**（或 Forward Roll / Sprinting Forward Roll / Dive Roll） | 翻滚闪避（闪键） | 程序化前滚翻 |
| **Stunned**（或 Dizzy） | 破绽/踉跄硬直 | 受击动作慢放 |
| **Great Sword Casting**（或 Warming Up / Taunt） | 重键蓄力姿态 | 施法聚气 |
| **Leg Sweep** | 扫堂腿（蹲+腿） | 空翻踢低位 |

可选增强（有则更好，无不影响）：

| 动作名 | 生效位置 |
| --- | --- |
| Great Sword Idle | 持剑临战待机（现用 Fighting Idle） |
| Great Sword Walk / Great Sword Run | 持剑走/跑（现用 Walking/Running） |
| Great Sword Impact | 持剑受击（现用 Hit Reaction） |
| Great Sword Death | 持剑死亡（现用 Dying） |

> 下载设置：Format=FBX for Unity，Skin=With Skin 或 Without Skin 均可，帧率 30，不勾 In Place。

---

## 一·续、第二角色 + 武器库（角色与武器资产分离）

游戏内右上「角色」按钮打开**角色·武器库面板**：先选角色，再从武器库选武器拿在手中
（默认持剑，重选即替换）。工程已内置 **glTFast** 包——`.glb` / `.gltf` 与 `.fbx`
一样放进去就能导入使用。目录契约（`Anims2/` 与 `Weapons/` 目录已在工程中建好）：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.fbx        ← 角色·壹（已就位）
├── PlayerModel2.glb       ← 角色·贰模型：glb / gltf / fbx 均可，文件名必须是 PlayerModel2
├── Anims/                  ← 角色·壹动作库（已就位）——角色·贰【沿用】这套动作库
├── Anims2/                 ← （可选）角色·贰专属动作库：留空=沿用 Anims/；
│                              放入片段则优先使用（清单与 Anims/ 相同，保留 @后缀）
└── Weapons/                ← 武器库：每个模型文件 = 一件武器，文件名即游戏内武器名
    ├── 长剑.glb            支持 .glb / .fbx / .gltf（gltf 连同 .bin 与贴图一起放）
    ├── 巨剑.fbx            武器数量 = 目录下模型文件数量，放几件就出现几件
    └── 武器合集.zip        也可直接丢 zip：编辑器自动解压出其中的模型/贴图并删除压缩包
```

规则与自动处理：

- **角色·贰（glb）**：`Resources.Load` 按名加载与扩展名无关，`PlayerModel2.glb` 放入即被识别；
  模型没有 Animator 时运行时自动补挂。**动作库沿用角色·壹**（`Anims/`）。
  **异源骨架自动对齐**：glb 角色骨名常无 mixamorig 前缀（Hips/Spine/LeftArm…，
  ReadyPlayerMe 等标准人形均如此）或根链路不同——运行时自动把骨骼改名并把链路对齐到
  参考骨架，让默认动作库直接绑定生效；仅当骨架与人形标准差异过大（匹配骨数过少）
  才放弃对齐（此时模型会保持静止，换回角色·壹即恢复）。`PlayerModel2` 缺失自动回退角色·壹。
- **武器库（glb/fbx/gltf/zip）**：放入即出现在面板中。
  - **zip**：每个 zip 自动解压到【以 zip 名命名的子目录】且保留包内相对路径——
    多个 zip 里同名的 scene.gltf/贴图互不覆盖；包内唯一的模型文件会被重命名为 zip 名
    （游戏内武器名 = zip 文件名）；解压完成后 zip 自动删除。
  - **装备**：自动隐藏角色模型自带兵器、把新武器挂到右手，并做**定尺 + 握持对齐**——
    最长轴视为刃轴、离模型原点近的一端视为柄端（可在武器预制体里放名为 `Grip` 的
    子节点显式指定柄位），按手部骨骼（手指/拇指）推算拳眼方向，把柄端放进手心：
    握在剑柄上而非刀刃上。刃长按角色体型归一（≈1.35m）。
  - **默认佩剑**：优先用模型自带兵器；模型没有自带兵器（glb 角色常见）时自动生成
    一把程序化长剑兜底——任何角色选「默认佩剑」手里都有剑。
- 选择本地持久化，重启保留。

---

## 二、然后……没有然后了

- 放进去后 Unity 会自动导入：**FBX 自动设为 Humanoid，走/跑/待机自动加 Loop**
  （由 `Assets/_Project/Editor/MixamoImportPostprocessor.cs` 完成）。
- 如果你是**先放的文件、后拉的这次代码**，对着
  `Assets/_Project/Resources/Characters` 文件夹右键 → **Reimport** 一次即可让自动设置生效。
- 直接 **Play**：玩家/敌人就换成 Mixamo 动捕动作了。控制台若报缺 Idle/Walk/Run，
  说明这三个基础片段没放对（其余动作缺了会自动跳过、不报错）。

---

## 三、动作 → 招式 对应表（代码里已配好，供你核对）

| 招式 | 采用的 Mixamo 动作 |
| --- | --- |
| 待机 / 临战待机 | Idle / Fighting Idle |
| 走 / 跑 | Walking / Running |
| 拳键一段·巨剑横斩 | Great Sword Slash |
| 拳键二段·巨剑撩斩 | Great Sword Slash (1) |
| 拳键三段·突刺（含前+重疾影突刺） | Stabbing |
| 拳键四段·巨剑旋风斩（含左右+重、旋风终结） | Great Sword High Spin Attack |
| 蓄力重击·巨剑跳劈 / 空袭跳劈（跳+拳、跳+重） | Great Sword Jump Attack |
| 腿键连段·正踢→侧踹→旋身空翻踢→飞踢 | Kicking / Side Kick / Spin Flip Kick / Flying Kick |
| 后+重·旋身空翻踢 / 蹲+攻·扫堂腿 | Spin Flip Kick |
| 直拳 / 交叉重拳（敌人拳系） | Lead Jab / Cross Punch |
| 受击 / 倒地 / 死亡 | Hit Reaction / Knocked Down / Dying |
| 施法 / 蓄力聚气 | Spell Casting |

播放层带**起手偏移 + 提速**：从片段的发力相位起播、按招式各自的倍速播放，
按键当拍出手、命中后立即可取消接招——连点即无缝连段。

绝招「觉醒·乱舞」会自动把上面几招串成连段演出（配酷炫但不遮挡动作的特效）。
缺哪个动作就自动跳过该招（回到 locomotion），不影响运行。

---

## 四、兵器（大剑）

已改为**自动**，不再挂那把程序化方块剑：

1. 若你下载的角色模型**本身就握着大剑**（Mixamo「Great Sword Pack」的角色多为持械导出），
   直接用模型自带的剑——代码会在骨骼层级里按名字（sword/blade/greatsword…）找到它并绑定刀光，
   **无需任何操作**。
2. 若模型不带剑、你想额外挂一把素材大剑：把大剑做成一个预制体命名为 `Weapon`，
   放到 `Resources/Characters/Weapon.prefab`，代码会实例化并挂到右手骨骼。
3. 两者都没有 → 右手不挂武器（徒手，正常，不会再出现悬空的方块剑）。

---

## 五、提交打包

Unity 里能正常 Play 后，把 `Assets/_Project/Resources/Characters/`（模型、动作 FBX 及其
`.meta`）整个提交推送到 `claude/**` 或 `main`，CI 会打出带动捕动作的 APK 到真机测试。

---

## 六、常见问题（本次已修）

| 现象 | 说明 |
| --- | --- |
| **角色腾空/陷地** | 已自动修：装配时量测模型包围盒，把脚底对齐到角色胶囊体底部。 |
| **角色太小** | 已自动修：自动缩放到标准身高 ~1.85m（大体型敌人在此基础上按体型放大）。 |
| **手里多一把方块剑** | 已自动修：动捕模式不再挂程序化剑，见上面「四、兵器」。 |
| **敌人太容易被打死** | 已加**调试模式**：默认开启「敌人耐揍」（设置面板可关）。正式发布把 `GameDebug.TankyEnemies` 设 false。 |
| **走路腿反向/像「鞋穿反了」** | 已自动修：动作 FBX 不带蒙皮、自建 Avatar 的 T-Pose 常校准失败导致重定向后腿部扭曲。现在 Anims/ 下的动画统一**复用 PlayerModel 的 Avatar**。操作：对 `Resources/Characters` 文件夹右键 → **Reimport**（先有 PlayerModel 再导动画）。 |

---

有任何一招时机/朝向不对，或想调整绝招连段顺序，把片段名发我，我改映射与串招。
