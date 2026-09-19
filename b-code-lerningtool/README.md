# 四级抄写生成工具（Cet4LearningTool）

寄宿在本仓的极简 Vulcan 模块，不进宿主发布登记表。

- 读 `a1-四级单词/U*.docx`，按「L1 1-1」「1-2」这类段落切出小单元（U1-1…）。
- 在 Aurora「四级抄写」页选中一行点「生成抄写」，经 `apollo.chat.send` 让 AI 用该小单元的单词造文，
  以 `a2-四级翻译/四级翻译短篇抄写n.docx` 为模板写出 `四级翻译短篇抄写<下一个编号>-U1-1.docx`。
- 单词必须全部用上（可写长或分两篇）：程序核对，缺词就交回 AI 修改，最多 3 轮，仍不齐不出文件。
- 英文译文里的单元单词由程序统一加粗。判据只认原形与规则词尾（-s/-ed/-ing/-er/-est 等），见 `WordMatcher`。
- 控制台同样可用：`cet4.copy.generate unit=U1-1`。

依赖：运行中的 HistoryVulcan 宿主，已装 Aurora 与 Apollo，且 Apollo 已配好密钥。

部署（构建、打包、热装到活宿主）：

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-code-lerningtool\Deploy.ps1
```

改版本时同时改 `Cet4LearningTool.csproj` 的 `Version` 与 `module.manifest.json` 的 `version`。
