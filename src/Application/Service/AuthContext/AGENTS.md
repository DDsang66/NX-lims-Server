# Application/Service/AuthContext — 认证用例编排

> 本文件是该目录的**协作入口文档**。读完即可掌握：本服务的职责边界、5 个用例方法的契约、
> 错误码映射、密码明文约束、修改日志与未来优化方向。
>
> 最后更新：src 内认证功能首次落地。

---

## 1. 目录定位

`AuthContext` 是按聚合划分的应用服务目录（与 `TerminalContext`、`DataSheetContext` 同构）。
本目录只放**用例编排**：串流程、翻译错误码、组装响应；业务规则一律委托领域与端口。

命名用 `AuthContext` 而非 `UserService`，避免与 src 外历史目录 `Application\Services\UserService` 混淆。

---

## 2. 类职责

### `AuthAppService`

- 命名空间：`NX_lims_Softlines_Command_System.src.Application.Service.AuthContext`
- 声明：`public sealed class AuthAppService : IScopedDependency`
- 生命周期：**Scoped**（由 `AddAutoRegister` 依据标记接口自动注册，无需手写 `Program.cs`）
- 依赖：`IUserRepository`、`IPasswordHasher`、`ITokenService`、`ILogger<AuthAppService>`
- 硬约束：**不得**直接拼 SQL、**不得**直接构造 JWT、**不得**把明文密码写入日志

---

## 3. 方法契约

| 方法 | 入参 | 返回 | 抛出的 `AuthException` |
|---|---|---|---|
| `LoginAsync(LoginRequestDto, string? lastLoginIp, CancellationToken)` | 登录请求 + 来源 IP | `LoginResponseDto` | `AUTH_001`（账号/密码错误，含账号不存在）、`AUTH_002`（已锁定） |
| `RefreshAsync(RefreshTokenRequestDto, CancellationToken)` | 刷新令牌 | `LoginResponseDto` | `AUTH_006`（令牌无效/过期/类型不符/用户不存在）、`AUTH_002` |
| `RegisterAsync(RegisterRequestDto, CancellationToken)` | 注册请求 | `Task`（无返回值） | `AUTH_004`（账号已存在）、`AUTH_005`（强度不足/账号为空） |
| `AdminResetAsync(AdminResetPasswordRequestDto, CancellationToken)` | 目标用户标识 | `AdminResetPasswordResponseDto` | `AUTH_003`（用户不存在） |
| `ChangePasswordAsync(ChangePasswordRequestDto, CancellationToken)` | 改密请求 | `Task`（无返回值） | `AUTH_003`、`AUTH_007`（当前密码错）、`AUTH_008`（新旧相同）、`AUTH_005` |

### 3.1 `LoginAsync` 的判定顺序（不可调换）

1. 账号/密码为空 → `AUTH_001`（不提示「账号为空」，防枚举）。
2. 用户不存在 → `AUTH_001`（同样不区分，防枚举）。
3. `!IsEnabled` → `AUTH_002`。
4. 密码校验失败 → 调用 `RegisterLoginFailureAsync` 原子自增；达到 `UserLoginPolicy.LockThreshold` 记告警日志；**统一**抛 `AUTH_001`（不回传「第几次失败」）。
5. 校验成功 → `ResetLoginFailureAsync` 清零并更新登录信息 → `IssueTokens` 签发。

### 3.2 `RefreshAsync` 的关键取舍

角色与启用状态**一律回库读取**，不采信刷新令牌内的旧值。
原因是角色可能在两次刷新之间被管理员变更，若沿用令牌旧值会造成权限滞留。

### 3.3 临时密码生成规则

`GenerateTemporaryPassword()` 使用 `RandomNumberGenerator`（密码学安全随机源），
16 位，先保证「大写 / 小写 / 数字 / 符号」四类各出现一次以满足 `PasswordPolicy`，
再 Fisher-Yates 打乱顺序；字符集剔除 `I/O/l/0/1` 等易混淆字符，降低人工转述出错概率。

---

## 4. 错误码与 HTTP 映射

映射由控制器 `UserAuthController.ToProblem` 完成（本层只负责抛带码异常）：

| Code | HTTP |
|---|---|
| `AUTH_001` / `AUTH_006` | 401 |
| `AUTH_002` | 403 |
| `AUTH_003` | 404 |
| `AUTH_004` | 409 |
| `AUTH_005` / `AUTH_007` / `AUTH_008` | 400 |

---

## 5. 修改日志

| 变更 | 说明 |
|---|---|
| 新建本目录与服务 | 落地登录、刷新、注册、管理员重置、自助改密 5 个用例；错误码集中在 `AuthErrorCode`；临时密码用密码学安全随机源生成 |

---

## 6. 未来优化方向

1. **刷新令牌轮换与单次使用**：当前刷新令牌在有效期内可重复使用，无黑名单；若要支持「改密后旧令牌立即失效」，需引入令牌版本号或黑名单存储。
2. **强制改密闭环**：`must_change_password` 列已由 SQL 脚本提供，但本轮不读写；接入后需在 `LoginResponseDto` 增加标记并在控制器强制拦截其他业务接口。
3. **重置操作审计落库**：当前只写 `ILogger`，后续审计模块应把「谁在何时重置了谁、原因」写入独立表。
4. **登录失败计数的可观测性**：达到锁定阈值时仅有告警日志，可考虑推送运维通知。
5. **注册端点权限**：当前开放注册，若业务要求收紧，可在控制器加 `[Authorize(Roles = UserRoles.Admin)]`。
