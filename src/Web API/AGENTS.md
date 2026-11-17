# src/Web API — 接口层（新架构）

> 本文件是该目录的**协作入口文档**。读完即可掌握：控制器统一规格、现有控制器清单、
> 新增认证接口的契约、与老架构接口的共存关系、修改日志与未来优化方向。
>
> 最后更新：新增 `UserAuthController`（`api/v1/auth`）。

---

## 1. 目录定位

`src` 的接口层，只负责 HTTP 协议适配：收请求、做参数校验、调用 Application 用例、组织响应。
业务规则不写在这里。

控制器统一使用 `[ApiController]` + `[Route("api/...")]`，继承 `ControllerBase`，构造函数注入应用服务。

---

## 2. 控制器清单（按文件名）

| 文件 | 路由前缀 | 职责 |
|---|---|---|
| `Controllers\UserAuthController.cs` | `api/v1/auth` | **新增**：登录、刷新、注册、管理员重置密码、自助改密 |
| `AbrasionResistanceReportController.cs` | `api/...` | 耐磨报表 |
| `CheckListController.cs` | `api/...` | 检测单 |
| `ConditionPoolController.cs` | `api/...` | 条件池 |
| `DataSheetController.cs` | `api/...` | 数据单 |
| `EnumController.cs` | `api/...` | 枚举字典 |
| `FiberAnalysisController.cs` | `api/...` | 纤维分析 |
| `MenuController.cs` | `api/...` | 菜单 |
| `MoistureDryingRateController.cs` | `api/...` | 回潮率 |
| `ParamFormulaController.cs` | `api/...` | 参数公式 |
| `ParamRulesController.cs` | `api/...` | 参数规则 |
| `ParamStructureController.cs` | `api/...` | 参数结构 |
| `PhysicalWeightController.cs` | `api/...` | 物理克重 |
| `PhysicalWeightReportController.cs` | `api/...` | 物理克重报表 |
| `RfidController.cs` | `api/...` | RFID |
| `StandardController.cs` / `StandardFamilyController.cs` | `api/...` | 标准 / 标准族 |
| `TemplateController.cs` | `api/...` | 模板 |
| `TerminalController.cs` | `api/...` | 终端 |
| `TestItemController.cs` | `api/...` | 测试项 |
| `TestReportController.cs` | `api/...` | 测试报告 |
| `WorkSheetController.cs` | `api/...` | 工作单 |
| `YarnCountReportController.cs` | `api/...` | 纱支报表 |
| `UseCase\` / `Buyer\` / `WashLabel\` | 各自子路由 | 审核编辑排程校验 / 买家与手册 / 洗标识别 |

---

## 3. `UserAuthController` 契约（重点）

- 路由：`api/v1/auth`（**独立于老架构的 `api/auth`**，两套共存，便于前端灰度切换）
- 依赖：仅 `AuthAppService`（薄控制器，无业务逻辑、无 SQL、无哈希调用）
- 错误输出：捕获 `AuthException` → `ProblemDetails`，`Extensions` 中带 `code` 与 `subject`

| 方法 | 路由 | 鉴权 | 说明 |
|---|---|---|---|
| `Login` | `POST api/v1/auth/login` | 匿名 | 取 `HttpContext.Connection.RemoteIpAddress` 作为登录 IP 传入用例 |
| `Refresh` | `POST api/v1/auth/refresh` | 匿名 | 刷新令牌 |
| `Register` | `POST api/v1/auth/register` | 匿名 | 开放注册，角色固定为访客 |
| `AdminResetPassword` | `POST api/v1/auth/admin/reset-password` | `[Authorize(Roles = UserRoles.Admin)]` | 返回一次性临时密码 |
| `ChangePassword` | `POST api/v1/auth/change-password` | `[Authorize]` | 自助改密 |

### 3.1 错误码 → HTTP 映射（`ToProblem`）

| Code | HTTP |
|---|---|
| `AUTH_001` / `AUTH_006` | 401 |
| `AUTH_002` | 403 |
| `AUTH_003` | 404 |
| `AUTH_004` | 409 |
| `AUTH_005` / `AUTH_007` / `AUTH_008` | 400 |
| 其它 | 500 |

只捕获 `AuthException`，**不捕获通用 `Exception`**：未预期异常交给全局管道，避免吞掉真实故障。

---

## 4. 与老架构接口的共存说明

| 接口 | 位置 | 现状 |
|---|---|---|
| `api/auth/*` | `Interfaces\Controllers\AuthController.cs`（**src 外**） | 冻结保留，前端仍在调用；不新增功能 |
| `api/v1/auth/*` | `src\Web API\Controllers\UserAuthController.cs` | 新实现，认证逻辑全部在 src 内 |

两条链路使用**不同的令牌服务**（老 `JwtService` 不验签刷新令牌；新 `JwtTokenService` 强制验签 + 类型校验）。
前端切换时需注意：新链路签发的令牌含 `role` 声明（双写长 URI 与短名），老链路不含。

---

## 5. 修改日志

| 变更 | 说明 |
|---|---|
| 新增 `UserAuthController` | 5 个认证端点，路由 `api/v1/auth`；`AuthException → ProblemDetails` 统一映射；临时密码仅单次响应返回 |

---

## 6. 未来优化方向

1. **本目录暂无统一响应包装**：新认证接口返回原始 DTO / `ProblemDetails`，其它控制器可能返回 `Result<T>`，前端需分别适配；若要统一需在控制器层做适配而非改造 DTO。
2. **控制器无 `[Authorize]` 全局策略**：除认证接口外，其余控制器目前多为匿名（与老架构一致）；后续应逐步引入全局鉴权与角色策略。
3. **注册端点可收紧**：如需内部管控，把 `register` 改为 `[Authorize(Roles = UserRoles.Admin)]`。
4. **强制改密未闭环**：`must_change_password` 列已建但登录响应未暴露该标记，前端无法强制跳转改密页。
