# Infrastructure/Service/Security — 安全相关基础设施服务

> 本文件是该目录的**协作入口文档**。读完即可掌握：本目录承载什么、两个实现类的方法契约、
> 算法与令牌选型、与老架构的差异、修改日志与未来优化方向。
>
> 最后更新：认证功能落地（新增 `JwtTokenService`，`IdentityPasswordHasher` 接入自动注册）。

---

## 1. 目录定位

存放认证/加密类基础设施实现，供 Domain / Application 端口的具体落点使用。
本目录可依赖 ASP.NET Core 共享框架与第三方库，但 **Domain 不得反向依赖本目录**。

| 路径 | 内容 |
|---|---|
| `IdentityPasswordHasher.cs` | `IPasswordHasher` 端口实现（PBKDF2 / IdentityV3） |
| `JwtTokenService.cs` | `ITokenService` 端口实现（JWT 签发与校验） |

---

## 2. 类型职责与方法契约

### 2.1 `IdentityPasswordHasher`

- 声明：`public sealed class IdentityPasswordHasher : IPasswordHasher, ISingletonDependency`
- 生命周期：**Singleton**（无状态，由 `AddAutoRegister` 自动注册；**不再需要** `Program.cs` 手工 `AddSingleton`）
- 固定 `PasswordHasherCompatibilityMode.IdentityV3` + `IterationCount = 100_000`（PBKDF2-HMAC-SHA256）

| 成员 | 契约 |
|---|---|
| `string Hash(string plainPassword)` | 明文为空/空白抛 `ArgumentException`；返回含随机盐的 Base64 哈希 |
| `PasswordVerifyResult Verify(string hashedPassword, string plainPassword)` | 任一参数为空抛 `ArgumentException`；匹配返回 `Success`，旧算法/低迭代返回 `SuccessRehashNeeded`，不匹配或格式非法返回 `Failed` |

设计要点：

1. **旧哈希无需手写回退**：ASP.NET Core 的 `PasswordHasher<TUser>` 原生识别 `0x00` 开头的 V2 格式
   （即历史 `Microsoft.AspNet.Identity` 生成的 49 字节 SHA1/1000 次哈希），在 V3 模式下校验通过后
   自动返回 `SuccessRehashNeeded`。
2. **`FormatException` 视为校验失败**：库中存量可能是明文或损坏 Base64，直接抛出会把登录打成 500；
   返回 `Failed` 可让失败计数与账号锁定继续生效。

### 2.2 `JwtTokenService`

- 命名空间：`NX_lims_Softlines_Command_System.src.Infrastructure.Service.Security`
- 声明：`public sealed class JwtTokenService : ITokenService, ISingletonDependency`
- 生命周期：**Singleton**（内部 `JwtSecurityTokenHandler` 无状态，可复用）
- 配置来源：`Jwt` 配置节 —— `Issuer`、`Audience`、`Secret`（三者缺失抛 `InvalidOperationException`），
  `ExpireMinutes`（默认 120）、`RefreshExpireHours`（默认 168）

| 成员 | 契约 |
|---|---|
| `TokenPair IssueTokens(string userId, string userName, string role)` | 签发访问令牌与刷新令牌，返回含过期时间的令牌对 |
| `bool TryValidateRefreshToken(string refreshToken, out string userId)` | 校验签名 + 有效期 + **令牌类型必须为 `refresh`**，通过时输出 uid |

令牌 claim 结构：

| 令牌 | claim |
|---|---|
| 访问令牌 | `uid`、`name`、`role`（双写 `ClaimTypes.Role` 与短名 `role`）、`jti`、`iat`、`exp`、`iss`、`aud` |
| 刷新令牌 | `uid`、`token_type = refresh`、`jti`、`iat`、`exp`、`iss`、`aud` |

关键取舍：

1. **角色双写**：`ClaimTypes.Role`（长 URI）与短名 `role` 都写入。JwtBearer 的入站映射表只把短名映射为长 URI，
   两种都写可兼容任意一侧读取方式，从而**不必修改 `Program.cs`** 的 `TokenValidationParameters`。
2. **必须验签**：使用 `ValidateToken`，并在校验参数里设置 `RoleClaimType = "role"`、`ClockSkew = 1 分钟`。
   老架构 `JwtService` 的 `ReadJwtToken` 只解析不验签、刷新令牌无类型校验，本实现**刻意不复制该缺陷**。
3. **日志不落令牌内容**：校验失败只记录异常类型。

---

## 3. 修改日志

| 变更 | 说明 |
|---|---|
| 新增 `IdentityPasswordHasher` | 用 Identity V3（PBKDF2-SHA256 / 10 万次）替代老 `Microsoft.AspNet.Identity.PasswordHasher`（SHA1 / 1000 次） |
| 接入自动注册 | `IdentityPasswordHasher` 增加 `ISingletonDependency` 标记，`IPasswordHasher` 由 `AddAutoRegister` 自动解析，移除 `Program.cs` 手工注册 |
| 新增 `JwtTokenService` | src 内独立的 JWT 签发/校验实现，角色 claim 双写、刷新令牌强制验签与类型校验 |

---

## 4. 未来优化方向

1. **刷新令牌轮换与吊销**：当前无状态 JWT，令牌在有效期内可重复使用；若要支持「改密后旧令牌失效」，
   需引入令牌版本号（存入用户表）或黑名单存储。
2. **算法与密钥强度**：`HmacSha256` + 配置文件明文密钥适合内网；若后续要求非对称签名，可切换 RS256 并把公钥分发。
3. **多节点一致性**：部署多实例时需确认所有节点的 `IterationCount` 与 `Jwt:Secret` 一致，否则会出现
   同一账号在不同节点反复 rehash 或令牌互不认可。
4. `Microsoft.AspNet.Identity.Core 2.2.4` 仍被老 `AuthController` 使用（老架构冻结），待老接口整体下线后可移除该包引用。
