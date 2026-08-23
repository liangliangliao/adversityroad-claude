# 本地体检脚本（提交前跑一遍，省一轮 CI 往返）

CI 一轮编译要 5 分钟左右，而下面几类错误在本地几秒就能查出来。
它们**不是**格式检查，每一条都对应一个真实发生过、并且让 CI 红过的编译错误。

```bash
python3 tools/cslint.py      # CS8641：else 找不到它的 if（括号平衡查不出来）
python3 tools/csquote.py     # CS1010：字符串/字符字面量里混进了真换行
python3 tools/csshadow.py    # CS0136：内层局部名与外层重名（C# 不看先后）
python3 tools/csident.py     # CS0103：受体写成了 anim / _anim 里错的那一个
python3 tools/csobsolete.py  # CS0619：用了本 Unity 版本 obsolete-as-error 的 API
python3 tools/animaudit.py   # 动作库里有没有"放进来了却没人用"的 FBX
python3 tools/resolvesim.py  # 复刻运行时解析顺序，推出每个 FBX 落到哪个槽位
```

`csquote.py` 是被下面这件事逼出来的：用脚本改源码时，脚本里写的 `\n`
会被脚本语言自己解释成真换行、直接写进 C# 字面量里。人眼几乎看不出来，
`cslint`（结构）与 `csshadow`（作用域）也都查不到。

`csident.py` 是被另一件事逼出来的：两个文件的字段名一个叫 `_anim`、一个叫 `anim`，
而脚本的锚点字符串对两边都匹配，于是补上去的那行用错了名字。
它只报"差一点点就对"的情况（本文件里存在 `_x`/`x` 的近邻声明）——
不加这道门槛全库 500+ 误报，那样的清单没人会看。

`csshadow.py`（几十处）与 `csident.py`（23 处）都有**已知误报基线**：
本地没有 C# 工具链，做不了真正的作用域/名字解析。
两者只适合当**改动文件**的提醒用——看的是"数字有没有比改动前变大"，
不要指望它们全绿。
