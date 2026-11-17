# Domain/Aggregeates/UserContext — 用户聚合上下文

> 本文件是该目录的**协作入口文档**。读完即可掌握：本目录承载什么、每个类型的职责与方法契约、
> 密码与主键设计、修改日志与未来优化方向。
>
> 最后更新：移除 `UserKey` 双标识冗余，聚合主键 `UserId` 即数据库主键。

---

## 1. 目录定位

存放**用户聚合**及其值对象、领域端口与规则常量。依赖方向遵守 DDD：本目录只能依赖 `src\Domain\Share`，
**不得**引用 Infrastructure、EF、SQL 客户端或任何具体哈希/JWT 实现。

| 路径 | 内容 |
|---|---|
| `User.cs` | 用户聚合根 `User`（`AggregateRoot<UserId, Guid>`） |
| `IUserRepository.cs` | 用户持久化端口 `IUserRepository` |
| `IPasswordHasher.cs` | 密码哈希端口 `IPasswordHasher` + 枚举 `PasswordVerifyResult` |
| `UserRoles.cs` | 角色常量 `UserRoles`（`Visitor` / `Admin`） |
| `UserLoginPolicy.cs` | 登录策略常量 `UserLoginPolicy`（`LockThreshold = 5`） |
| `ValueObj\UserId.cs` | 用户聚合主键值对象 `UserId` |
| `ValueObj\PasswordPolicy.cs` | 密码强度策略 `PasswordPolicy` |

---

## 2. 类型职责与方法契约

### 2.1 `User`（聚合根）

关键设计：**聚合主键 `UserId` 即数据库主键**。持久化时由仓储序列化成 32 位十六进制字符串写入
`user_id` 列，读取时反序列化回 `UserId`；聚合内不维护第二套持久化标识。

| 成员 | 契约 |
|---|---|
| `UserName` / `PassWord` / `Role` / `RealName` / `EmployeeId` / `NickName` | 只读属性。**`PassWord` 语义为哈希值，禁止存明文** |
| `Status` / `LoginFailCount` / `CreateTime` / `UpdatedTime` / `LastLoginIp` | 登录状态相关只读属性，由仓储回填 |
| `IsEnabled` | `Status == 1` 的便捷判断 |
| `User(UserId id, string userName, string hashedPassword, string role, string realName, string employeeId, string? nickName = null, int status = 1, int loginFailCount = 0, DateTime? createTime = null, DateTime? updatedTime = null, string? lastLoginIp = null)` | 供仓储恢复或 `Create` 内部调用；`hashedPassword` 必须是哈希值 |
| `static Create(userName, plainPassword, role, realName, employeeId, IPasswordHasher, nickName?)` | 创建新用户：`PasswordPolicy.EnsureValid` 拦截弱密码 → `passwordHasher.Hash` → 生成 `UserId`。**聚合内不保留明文** |
| `bool VerifyPassword(string plainPassword, IPasswordHasher)` | 校验明文；仅 `PasswordVerifyResult.Failed` 判为不通过（`SuccessRehashNeeded` 视为密码正确） |
| `void ChangePasswordHash(string passwordHash)` | 用已计算好的哈希替换 `PassWord` 并刷新 `UpdatedTime` |
| `void ChangePassword(string newPlainPassword, IPasswordHasher)` | 强度校验 + 哈希 + 委托 `ChangePasswordHash` |
| `void MarkLoginSucceeded(string? lastLoginIp)` | 内存侧登录成功态：失败计数归零、刷新时间与来源 IP（数据库侧由仓储原子 SQL 完成） |

**异常约定**：`passwordHasher` 为 null 抛 `ArgumentNullException`；账号/角色为空、密码不满足强度、
哈希为空抛 `ArgumentException`，消息为可直接展示给操作人员的中文原因。

### 2.2 `IUserRepository`（领域端口）

| 成员 | 契约 |
|---|---|
| `FindByUserNameAsync(string, CancellationToken)` | 按账号精确查；未找到返回 null |
| `FindByIdAsync(UserId, CancellationToken)` | 按聚合主键查 |
| `FindByIdentifierAsync(string, CancellationToken)` | 按「账号 / 工号 / 昵称」任一匹配，账号优先（保留历史重置接口多字段口径） |
| `ExistsByUserNameAsync(string, CancellationToken)` | 账号是否存在 |
| `AddAsync(User, CancellationToken)` | 新增记录，`PassWord` 必须是哈希 |
| `UpdatePasswordAsync(UserId, string passwordHash, CancellationToken)` | 更新密码哈希 |
| `RegisterLoginFailureAsync(UserId, int lockThreshold, CancellationToken)` | 原子自增失败计数，达阈值同时锁定，返回累计次数 |
| `ResetLoginFailureAsync(UserId, string? lastLoginIp, CancellationToken)` | 失败计数归零并更新登录信息 |

所有方法以聚合主键 `UserId` 为标识；**序列化形式由实现方负责**，端口不暴露存储细节。

### 2.3 `IPasswordHasher`（领域端口）

`string Hash(string plainPassword)` / `PasswordVerifyResult Verify(string hashedPassword, string plainPassword)`。
返回 `Failed` / `Success` / `SuccessRehashNeeded`。实现位于
`src\Infrastructure\Service\Security\IdentityPasswordHasher.cs`。

### 2.4 `UserLoginPolicy`（策略常量）

`LockThreshold = 5` —— 连续登录失败达到该次数后账号被锁定。
**单点定义**：应用层编排与基础设施 SQL 都从这里取值，不得在别处重复写 5。

### 2.5 `PasswordPolicy`（强度策略）

`MinLength = 8`、`MaxLength = 128`，要求字母与数字并存。`EnsureValid` 违规抛带中文原因的 `ArgumentException`。

---

## 3. 与运行时链路的关系（重要事实）

- `src` 内认证接口（`api/v1/auth/*`）已通过 `AuthAppService` → `IUserRepository` 真正落地使用本聚合。
- `src` 外的老 `Interfaces\Controllers\AuthController.cs` 使用的是 EF 实体 `Domain\Model\Entities\User`，
  **与本聚合无关**，两套模型并存（老接口保持冻结）。
- 因此本目录与老架构之间不存在编译或运行依赖。

---

## 4. 修改日志

| 变更 | 说明 |
|---|---|
| 密码哈希端口化 | 新增 `IPasswordHasher` / `PasswordVerifyResult` 与 `ValueObj\PasswordPolicy`；`User` 不再接收明文构造 |
| 新增 `UserRoles` | 集中角色常量，`Admin` 值同时用于 `[Authorize(Roles = ...)]` |
| 认证功能落地 | 新增 `IUserRepository` 端口与 `UserLoginPolicy`；`User` 扩展 `NickName`/`Status`/`LoginFailCount`/时间与 IP 只读成员，新增 `ChangePasswordHash`、`MarkLoginSucceeded`、`IsEnabled` |
| 移除 `UserKey` 双标识 | 原设计为迁就 varchar 主键而在聚合内维护 `UserKey`，属于把历史设计带入新架构；现改为聚合主键 `UserId` 即数据库主键，端口与实现统一切换，`FindByUserKeyAsync` 改名为 `FindByIdAsync` |

---

## 5. 未来优化方向

1. **同类问题未清理**：`src` 内其它聚合（`CheckList`、`DataSheet`、`ConditionPool`、`DataSheetBatch`）仍在应用层/领域工厂用 `Guid.NewGuid()` 手动生成主键，与本次修正属同类问题，按约定本次未改动，建议后续统一评估。
2. **`RealName` 尚未落地**：`user` 表无该列，仓储回填为空串；若需要应另查 `user_profile`。
3. **强制改密状态未进入聚合**：`must_change_password` 列已建但未读写；接入时建议在聚合增加 `MustChangePassword` 只读属性与相应行为方法。
4. **`PasswordPolicy` 与 `User` 缺单元测试**：工作区暂无测试项目，建议覆盖边界长度、纯字母、纯数字、中文密码。
5. **角色当前为字符串常量**：若要支持权限矩阵，应升级为枚举/权限值对象。
