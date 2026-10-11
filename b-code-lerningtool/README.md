# 四级抄写生成工具（Cet4LearningTool）

寄宿在本仓的极简 Vulcan 模块，不进宿主发布登记表。

- 读 `a1-四级单词/U*.docx`，按「L1 1-1」「1-2」这类段落切出小单元（U1-1…）；「L1」单独占一行也认。
  同一本书同一个 L 标记下的小单元归成一关，编号带书号：U1-L1 = U1-1~U1-4（每本书都有 L1）。
- 在 Aurora「四级抄写」页选中一关点「生成抄写」：程序**逐个小单元**经 `apollo.chat.send` 让 AI 造文（每次只交一个小单元的单词，调用轻），
  全部合格后合成一份，以 `a2-四级翻译/四级翻译短篇抄写n.docx` 为模板写出 `四级翻译短篇抄写<下一个编号>-U1-L1.docx`。
- 单词必须全部用上：每个小单元各自核对，缺词就交回 AI 修改，最多 3 轮；任何一个小单元仍不齐，这一关不出文件。
- 英文译文里的单词由程序统一加粗。判据只认原形与规则词尾（-s/-ed/-ing/-er/-est 等），见 `WordMatcher`。
- 同一关可以反复生成，每次写一份新编号的文件；旧文件（含 0.4.0 以前按小单元生成的）与本次前面小单元的标题都交给 AI，要求换话题。
  表里「次数」是这一关已生成份数，「已生成」是最近一份，点它直接用 Word 打开。生成完表格自动刷新。
- 控制台同样可用：`cet4.copy.generate level=U1-L1`、`cet4.copy.open file=四级翻译短篇抄写25-U1-L1.docx`。

依赖：运行中的 HistoryVulcan 宿主，已装 Aurora 与 Apollo，且 Apollo 已配好密钥。

部署使用宿主模块开发手册的 start → submit → 人工审批 → finish。
7.0.0 的运行包为 Cet4LearningTool.nupkg；旧 Deploy.ps1 不再构建或安装目录包。
规范指令域为 cet4learningtool，原 cet4.* 五条命令仍兼容；页面归属为 Cet4LearningTool。
学习资料按宿主 vulcan.host.info 的 libraryRoot 定位主树，不再写死磁盘路径。
构建前通过管线或显式 -p:HistoryVulcanPackageRoot 配置宿主发布根（包含 host）。
版本源 Cet4Version.props；改版同步根 project.manifest.json 和 module.manifest.json。
离线验证：dotnet run --project b-Code-Tests/Cet4LearningTool.Tests.csproj -c Release -p:HistoryVulcanPackageRoot=<发布根>。