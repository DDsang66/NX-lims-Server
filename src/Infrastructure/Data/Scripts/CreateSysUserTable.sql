/* ============================================================================
   脚本名称：CreateSysUserTable.sql
   目标库  ：SQL Server —— src 新架构自有库（与老架构 [user] 表所在库无关，互不影响）
   目标表  ：dbo.sys_user
   执行时机：在把数据库模型反向工程到后端【之前】执行一次
   幂等说明：整段以 IF OBJECT_ID(N'dbo.sys_user', N'U') IS NULL 包裹，已存在则跳过

   反向工程提示：
       建表完成后，用 EF Core Scaffold（或 VS 的「反向工程」向导）连接本库，
       选择 sys_user 表生成实体与 DbContext，把实体落到
       src\Infrastructure\Data\Persistence，再基于生成的模型补 IUserRepository 实现。

   字段说明：与领域聚合 src\Domain\Aggregeates\UserContext\User.cs 一一对应。
   回滚    ：见文件末尾「回滚段」。
   ============================================================================ */

IF OBJECT_ID(N'dbo.sys_user', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sys_user
    (
        -- 聚合主键，存 32 位十六进制字符串（Guid 的 N 格式），长度满足 varchar(50)
        user_id              VARCHAR(50)   NOT NULL,

        -- 登录账号，全局唯一
        user_name            VARCHAR(50)   NOT NULL,

        -- PBKDF2 密码哈希（Base64 约 84 字符，留足余量），禁止存明文
        pass_word            VARCHAR(500)  NOT NULL,

        -- 昵称，可作为登录标识匹配
        nick_name            NVARCHAR(50)  NULL,

        -- 工号，可作为登录标识匹配
        employee_id          VARCHAR(50)   NULL,

        -- 角色：Visitor（注册默认）/ Admin（可重置他人密码）
        role                 VARCHAR(50)   NOT NULL
            CONSTRAINT DF_sys_user_role DEFAULT ('Visitor'),

        -- 账号状态：1 启用，0 锁定或停用
        status               TINYINT       NOT NULL
            CONSTRAINT DF_sys_user_status DEFAULT (1),

        -- 连续登录失败次数，登录成功归零，达到阈值时 status 被置 0
        login_fail_count     INT           NOT NULL
            CONSTRAINT DF_sys_user_login_fail_count DEFAULT (0),

        create_time          DATETIME      NULL,
        updated_time         DATETIME      NULL,
        last_login_ip        VARCHAR(50)   NULL,

        -- 是否强制下次登录改密（管理员重置后置 1）
        must_change_password BIT           NOT NULL
            CONSTRAINT DF_sys_user_must_change_password DEFAULT (0),

        -- 最近一次被重置密码的时间
        password_reset_at    DATETIME2(0)  NULL,

        -- 执行重置的管理员用户 ID
        reset_by             NVARCHAR(64)  NULL,

        -- 重置原因 / 关联工单号
        reset_reason         NVARCHAR(256) NULL,

        CONSTRAINT PK_sys_user PRIMARY KEY CLUSTERED (user_id)
    );

    CREATE UNIQUE INDEX UX_sys_user_user_name ON dbo.sys_user (user_name);
END
GO

/* ----------------------------------------------------------------------------
   第一个管理员账号怎么来（二选一，脚本不自动执行）

   方案一（推荐）：
       1) 先调 POST api/v1/auth/register 注册一个账号，角色为 Visitor；
       2) 再执行：
              UPDATE dbo.sys_user SET role = 'Admin' WHERE user_name = N'<账号>';
       3) 该账号必须重新登录 —— 角色声明写在 JWT 里，旧令牌仍带 Visitor。

   方案二：
       手工 INSERT 也可以，但 pass_word 必须是系统生成的 PBKDF2 哈希串。
       不要手写或伪造哈希值，否则该账号无法登录。
   ---------------------------------------------------------------------------- */

/* ----------------------------------------------------------------------------
   回滚段（人工执行，先备份数据再启用）

   DROP INDEX UX_sys_user_user_name ON dbo.sys_user;
   DROP TABLE dbo.sys_user;
   ---------------------------------------------------------------------------- */
