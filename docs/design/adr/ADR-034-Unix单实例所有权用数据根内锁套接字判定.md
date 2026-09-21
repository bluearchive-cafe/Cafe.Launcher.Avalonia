# ADR-034：Unix 单实例所有权用数据根内锁套接字判定，不用 .NET 命名互斥量

- 状态：✅ 已接受
- 日期：2026-09-21
- 来源：用户报告「Linux 单实例无法正常工作」——修复 show 信号跨平台化后仍可启动两个实例
- 相关：ADR-025（数据根由组合根解析并注入，锁套接字落在数据根内）

## 背景

单实例门原是跨平台的一行：`new Mutex(true, @"Local\Cafe_Launcher_SI", out var createdNew)`。
在 Linux 实机上这行**从未真正去重过**。

实测证据（Arch Linux，.NET 10）：.NET 在 Unix 上把命名同步对象的状态文件放在
`$TMPDIR/.dotnet/shm/` 下，`Local\` 前缀被映射成**按 POSIX 会话（`getsid`）隔离**的
`session<N>/` 目录。同一台机器上同时出现了 `session731520`、`session732230` 等多个目录——
每个启动上下文一个：

| 启动方式 | POSIX 会话 | 互斥量状态目录 | 结果 |
| --- | --- | --- | --- |
| 桌面/终端 A 会话启动 | sid X | `sessionX/` | 实例 A 获胜 |
| `setsid`/systemd/另一终端启动 | sid Y ≠ X | `sessionY/` | 实例 B **也**获胜 |

两个实例各拿各的互斥量，`createdNew` 都为 true——**任何启动环境差异（桌面图标 vs
终端 vs systemd 用户单元 vs 应用内重启）都会产生一对互不可见的互斥量**，双开。
同 shell 内并发启动之所以能去重（探测 10/10 单赢家），只是因为两个进程恰好共享
同一个 POSIX 会话；这也是此缺陷此前未被单元测试发现的原因——测试进程内不存在会话切换。

## 决策

1. **所有权判定分平台**（`CrossProcessLaunchBridge.AcquireOwnership`）：
   - Windows 保持命名互斥量 `Local\Cafe_Launcher_SI`——Inno 安装器的 `AppMutex`
     依赖这个名字，且 Windows 的 `Local\` 语义（按登录会话）正是想要的按用户去重。
   - Unix 改用**数据根内锁套接字的内核原子 `bind`**（`CrossProcessLaunchSignal.TryBindExclusive`）：
     绑定成功即获胜并持有监听直至 Dispose；截止时间内端口后始终有存活监听者即判负。
     数据根（XDG data home）与启动上下文无关，天然跨 POSIX 会话共享。

2. **锁套接字复用信号传输的全部机制**：同一 `GetSocketFilePath` 派生（文件名短、有
   ≤107 字节机械守卫）、同一「活性探测 + 短暂重试」收敛（旧实例退场窗口、崩溃残留的
   陈旧文件删除重绑）。进程死亡即释放锁，SIGKILL 也不会永久堵死。

3. **失败语义与信号传输刻意不同**：信号绑定失败可以降级（只损失转发），
   单实例门必须给出明确胜负。仅当套接字路径本身不可用（数据根所在文件系统不支持
   AF_UNIX 等）时才失败放行并记录警告——文件系统问题不应阻止用户启动应用。

4. ** Unix 上不再创建 .NET 命名互斥量**；`TryEnterSingleInstance(mutexName, …)` 的
   mutexName 参数只在 Windows 分支消费。

## 后果

- 单实例去重不再依赖 `/tmp/.dotnet/shm` 的会话命名空间，也不受 `TMPDIR` 差异影响
  （套接字路径由注入的数据根派生，进程内只解析一次）。
- 跨用户语义更合理：数据根按用户隔离，同一用户的多次启动去重，不同用户互不干扰
  （Windows `Local\` 在多登录会话下也是按会话去重，两侧一致）。
- 代价是 Unix 门的生命周期与进程绑定（内核保证），没有 Windows 互斥量的
  AbandonedMutex 概念可依赖——陈旧残留由既有的活性探测分支处理，已有测试覆盖。
- 回归验证靠三层：桥测试（跨平台、含陈旧锁文件恢复）、CI Linux job、以及实机
  `setsid` 双环境启动复验。
