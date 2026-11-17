# Infrastructure/Data/Scripts — 数据库脚本

> 本文件是该目录的**协作入口文档**。读完即可掌握：脚本清单、执行顺序、建表口径、
> 反向工程衔接方式与回滚方法。
>
> 最后更新：改为完整建表脚本 `CreateSysUserTable.sql`（表名 `sys_user`）。

---

## 1. 目录定位

存放随 src 内功能一同交付的**手工 SQL 脚本**。

本项目流程是「**先由脚本建表 → 再由反向工程生成后端模型**」，
所以脚本是起点而不是补丁：表结构一旦定稿，代码侧的实体与映射都从库里反向生成，
**不在代码里手写 EF 映射，也不手写 SQL 去迁就旧表**。

---

## 2. 脚本清单

| 文件 | 作用 | 影响对象 | 执行时机 |
|---|---|---|---|
| `CreateSysUserTable.sql` | 新建新架构自有的用户表 | `dbo.sys_user` | 反向工程**之前**，执行一次 |

### 2.1 `CreateSysUserTable.sql` 明细

- **表名 `sys_user`**：显式标记为新架构自有表，与老架构的 `[user]` 表完全独立（老表在另一个库）。
- **幂等**：整段以 `IF OBJECT_ID(N'dbo.sys_user', N'U') IS NULL` 包裹，已存在则跳过。
- **主键**：`user_id VARCHAR(50)`，存聚合主键（Guid 的 32 位十六进制串）。
- **唯一约束**：`user_name` 建唯一索引 `UX_sys_user_user_name`。
- **密码列**：`pass_word VARCHAR(500)`，存 PBKDF2 哈希（Base64 约 84 字符，留足余量），禁止明文。
- **登录状态**：`status`（1 启用 / 0 锁定）、`login_fail_count`、`updated_time`、`last_login_ip`。
- **强制改密与审计四列**：`must_change_password`、`password_reset_at`、`reset_by`、`reset_reason`
  —— **当前只建不读写**，属「强制改密 + 重置审计」能力的库侧预留。
- **管理员初始化**：脚本只给两条注释示例（注册后 `UPDATE role='Admin'`，或手工 `INSERT` 但哈希必须由系统生成），**不自动变更任何权限**。
- **回滚**：文件末尾以注释形式提供 `DROP INDEX` / `DROP TABLE`。

### 2.2 与反向工程的衔接

建表完成后，用 EF Core Scaffold（或 VS 的「反向工程」向导）连接本库、选择 `sys_user` 生成实体与
`DbContext`，实体落到 `src\Infrastructure\Data\Persistence`。之后再补
`src\Infrastructure\Data\Repositories\UserRepos` 下的 `IUserRepository` 实现
（详见该目录 `AGENTS.md`）。

---

## 3. 修改日志

| 变更 | 说明 |
|---|---|
| 曾有 `AddUserPasswordResetColumns.sql` | 内容是针对老 `[user]` 表的 4 段 `ALTER TABLE ... ADD`，属「改表」而非「建表」，已删除 |
| 新建 `CreateSysUserTable.sql` | 完整 `CREATE TABLE dbo.sys_user`，含领域模型全部字段与 4 个审计列，供反向工程使用 |

---

## 4. 未来优化方向

1. 4 个审计列仍未接入代码（`must_change_password` 等只建不读写），接入时需同步更新本文件与 `AuthContext` 文档。
2. 脚本数量增多后建议引入版本号命名（如 `V20260101__create_sys_user.sql`）与执行记录表，避免依赖人工判断是否已执行。
3. 老架构在仓库根 `Scripts\` 下另有历史脚本，长期应统一收敛，避免两处并存。
