# 本地体检脚本（提交前跑一遍，省一轮 CI 往返）

CI 一轮编译要 5 分钟左右，而下面几类错误在本地几秒就能查出来。
它们**不是**格式检查，每一条都对应一个真实发生过、并且让 CI 红过的编译错误。

```bash
python3 tools/cslint.py      # CS8641：else 找不到它的 if（括号平衡查不出来）
python3 tools/csquote.py     # CS1010：字符串/字符字面量里混进了真换行
python3 tools/csshadow.py    # CS0136：内层局部名与外层重名（C# 不看先后）
python3 tools/animaudit.py   # 动作库里有没有"放进来了却没人用"的 FBX
python3 tools/resolvesim.py  # 复刻运行时解析顺序，推出每个 FBX 落到哪个槽位
```

`csquote.py` 是被下面这件事逼出来的：用脚本改源码时，脚本里写的 `\n`
会被脚本语言自己解释成真换行、直接写进 C# 字面量里。人眼几乎看不出来，
`cslint`（结构）与 `csshadow`（作用域）也都查不到。

`csshadow.py` 全库有几十处误报（同级 foreach 各自作用域并不冲突），
只适合当**改动文件**的提醒用，不要指望它全绿。
