# 四级学习模块工作合同

模块 Cet4LearningTool 0.6.0，适用 HistoryVulcan 7.0.0；公开代码契约为 Core 6.0.0。
先读 project.manifest.json、README.md 和 b-Office/现行约定.md。
模块修改走宿主模块开发手册的 start / submit / finish，在 start 返回的 F 盘工作区实施。
只引用已发布 HistoryVulcan.Core，不引用宿主内部实现，不停止宿主，不手动安装运行包。
第一次成功 submit 后留给用户审批；未经审批不 finish、push 或正式发布。
学习资料是用户数据，升级验证不得修改 a1 / a2 / a3；测试只用临时夹具，不调用真实 AI。
维护代码目录 b-code-lerningtool 和测试目录 b-Code-Tests；不要顺手改学习资料。
版本 props、模块 manifest 和根 manifest 同步。跨模块指令说明只从 Diana 注册自描述读取。