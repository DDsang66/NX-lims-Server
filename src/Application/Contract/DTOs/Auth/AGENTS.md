# Application/Contract/Auth — 认证契约

> 本文件是该目录的**协作入口文档**。读完即可掌握：本目录承载什么、每个 DTO 的字段含义、
> 哪些字段携带明文密码、错误码约定、修改日志与未来优化方向。
>
> 最后更新：src 内认证功能首次落地。

---

## 1. 目录定位

存放认证用例的**输入/输出契约与业务异常**。只放不可变的 `record` 与常量/异常类型，
不含任何业务逻辑、不含任何基础设施依赖。

上层（`src\Web API`）用它收请求，下层（`src\Application\Service\AuthContext`）用它返回结果。

---

## 2. 文件与类型清单

| 文件 | 类型 | 说明 |
|---|---|---|
| `AuthRequests.cs` | `LoginRequestDto` / `RefreshTokenRequestDto` / `RegisterRequestDto` / `AdminResetPasswordRequestDto` / `ChangePasswordRequestDto` | 全部入参契约 |
| `AuthResponses.cs` | `UserProfileDto` / `LoginResponseDto` / `TokenPair` / `AdminResetPasswordResponseDto` | 全部出参契约 |
| `AuthErrorCode.cs` | `AuthErrorCode`（静态常量） | 稳定错误码 |
| `AuthException.cs` | `AuthException` | 携带错误码与业务标识的可读业务异常 |

---

## 3. 契约明细

### 3.1 入参

| 类型 | 字段 | 字段含义 |
|---|---|---|
| `LoginRequestDto` | `UserName` / `Password` | 登录账号 / **明文密码** |
| `RefreshTokenRequestDto` | `RefreshToken` | 刷新令牌串 |
| `RegisterRequestDto` | `UserName` / `Password` / `NickName` / `EmployeeId` | 账号 / **明文密码** / 昵称 / 工号 |
| `AdminResetPasswordRequestDto` | `UserIdOrName` | 目标用户的账号或工号；**请求中不传密码**，临时密码由系统生成 |
| `ChangePasswordRequestDto` | `UserNameOrEmployeeId` / `CurrentPassword` / `NewPassword` | 当前用户标识 / **明文当前密码** / **明文新密码** |

### 3.2 出参

| 类型 | 字段 | 字段含义 |
|---|---|---|
| `UserProfileDto` | `UserId` / `UserName` / `NickName` / `EmployeeId` / `Role` | 用户档案，**不含任何密码字段** |
| `LoginResponseDto` | `AccessToken` / `RefreshToken` / `AccessTokenExpiresAt` / `User` | 登录或刷新的结果 |
| `TokenPair` | `AccessToken` / `RefreshToken` / `AccessTokenExpiresAt` / `RefreshTokenExpiresAt` | 令牌签发结果（应用层内部使用） |
| `AdminResetPasswordResponseDto` | `UserId` / `TempPassword` / `ExpiresAt` | 重置结果；`TempPassword` 是**唯一出现明文密码的出参** |

### 3.3 明文密码边界（重要约束）

明文只允许出现在三处：`LoginRequestDto.Password`、`RegisterRequestDto.Password`、
`ChangePasswordRequestDto.CurrentPassword`/`NewPassword`，以及出参
`AdminResetPasswordResponseDto.TempPassword`。

**禁止**把上述字段写入日志、异常消息、审计字段或任何持久化位置。

---

## 4. 错误码表

| Code | 含义 | HTTP 映射（由控制器决定） |
|---|---|---|
| `AUTH_001` | 账号或密码错误（不区分账号不存在，防枚举） | 401 |
| `AUTH_002` | 账号已锁定或已停用 | 403 |
| `AUTH_003` | 目标用户不存在 | 404 |
| `AUTH_004` | 账号已存在 | 409 |
| `AUTH_005` | 密码强度不满足要求 | 400 |
| `AUTH_006` | 刷新令牌无效或已过期 | 401 |
| `AUTH_007` | 当前密码不正确 | 400 |
| `AUTH_008` | 新密码与当前密码相同 | 400 |
| `AUTH_009` | 认证基础设施未正确配置 | 500 |

---

## 5. 修改日志

| 变更 | 说明 |
|---|---|
| 新建本目录 | 随 src 内认证功能落地，包含 5 个入参 record、4 个出参 record、错误码常量与 `AuthException` |

---

## 6. 未来优化方向

1. `AUTH_009` 已定义但当前由基础设施层直接抛 `InvalidOperationException`，尚未翻译成该错误码；后续可在应用层统一包装。
2. `AdminResetPasswordResponseDto.ExpiresAt` 当前恒为 `null`（未实现临时密码时效），若要实现需配合 `must_change_password` 列落地。
3. 若前端需要统一响应包装（`Result<T>` 风格），应在控制器层适配，不在本目录改造 record。
