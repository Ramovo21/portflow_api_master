# PortFlow API (.NET 10)

Bản .NET của `../portflow` (Spring Boot 4.1), map 1:1 theo module Spring Modulith. Đã port **F00 – Foundation**,
**F01 – Identity & AuthZ** và **F03 – Workflow** (`docs/specs/` bên repo Java); các module còn lại mới có khung project.

`db/` chứa **toàn bộ** migration Flyway của bản Java (kể cả bảng của module chưa port) để schema giống hệt và
không vướng `outOfOrder=false` khi port dần các module sau.

## Chạy

```bash
docker compose --profile auth up -d                           # hạ tầng + Keycloak :8180 + migrate (kèm seed dev)
dotnet run --project src/PortFlow.Host --launch-profile dev   # api + worker trong một tiến trình
dotnet test --filter "Category!=Integration&Category!=Keycloak" # unit + kiến trúc
dotnet test --filter "Category=Integration&Category!=Keycloak" # cần Docker (Testcontainers + Flyway, JWT ký giả)
dotnet test --filter "Category=Keycloak"                      # Keycloak thật (chậm)
docker compose --profile auth --profile app up -d --build     # api :8080 + worker :8081 từ Dockerfile
```

Compose dùng cùng image/cổng với compose bản Java nên không chạy song song hai bộ. Lấy token dev:
xem `infra/keycloak/README.md` (user mẫu `sales01`…`admin`, mật khẩu `Portflow@123`).

## Profile

`ASPNETCORE_ENVIRONMENT` thay cho `SPRING_PROFILES_ACTIVE`, mặc định `api`. Biến môi trường cũ (`DB_URL` dạng JDBC,
`RABBITMQ_HOST`, `MINIO_ENDPOINT`, `SERVER_PORT`, `MANAGEMENT_PORT`, `KC_ISSUER`, `KC_JWKS`, `KC_SERVER_URL`,
`KC_SYNC_CLIENT_SECRET`, ...) vẫn được đọc.

| Profile | Web API | Listener RabbitMQ | Job | Cổng |
|---|---|---|---|---|
| `api` | ✓ | – | – | 8080 |
| `worker` | chỉ `/actuator` | ✓ | ✓ | 8081 |
| `dev` | ✓ | ✓ | ✓ | 8080 |

## Map Java → .NET (shared kernel)

| Spring (`com.portflow.shared`) | .NET (`PortFlow.Shared`) |
|---|---|
| `@Modulithic` + `allowedDependencies` | Project `PortFlow.<Module>.Api` (public) + `PortFlow.<Module>` (nội bộ); ProjectReference chỉ tới `.Api`; `ArchitectureTests.ModulesRespectBoundaries` |
| `AggregateRoot` (`@Version Long`) | `AggregateRoot<TId>`; EF interceptor gán `Version` 0 khi thêm, +1 khi sửa (concurrency token) |
| `Money`/`Quantity` (`BigDecimal`, HALF_UP) | `decimal`, `MidpointRounding.AwayFromZero`, giữ scale 2/3 |
| `DateRange`, `LocalDate`, `Instant`, `ZoneId` | NodaTime `LocalDate`, `Instant`, `DateTimeZone` |
| `ErrorCode` enum / `SysErrorCode` | `IErrorCode`, `ErrorCode` record, `SysErrorCode.SYS_*` |
| `@Transactional` / `TransactionTemplate` | `ITransactionRunner` trên `DbSession` (một kết nối + transaction cho mỗi scope, dùng chung cho EF Core và Dapper) |
| JPA repository / `JdbcClient` | EF Core (`AddPortFlowDbContext<T>`, snake_case) / Dapper |
| Flyway khi khởi động | Flyway CLI (container `flyway/flyway:11`) cùng thư mục `db/`; không dùng EF migration |
| `GlobalExceptionHandler` (ProblemDetail) | `IExceptionHandler` + `UseStatusCodePages` → `application/problem+json` có `errorCode`, `correlationId`, `errors` |
| Bean Validation | FluentValidation (`AbstractValidator<T>`), lỗi 422 `SYS_VALIDATION` |
| `CorrelationIdFilter` + MDC | `CorrelationIdMiddleware` + `AsyncLocal` + log scope |
| `@Idempotent` + aspect | `[Idempotent]` + action filter (cùng transaction, phát lại response, `Idempotent-Replayed`) |
| Spring Modulith Event Publication Registry | `DomainEventPublisher` ghi `shared.event_publication` cùng transaction; dispatcher sau commit; `IDomainEventListener<T>` (= `@ApplicationModuleListener`) |
| `@Externalized` + `ConfirmingRabbitMessagingTemplate` | `[Externalized]` + `RabbitEventExternalizer` (publisher confirm, timeout 10s) |
| `@RabbitListener` + `@IdempotentConsumer` | `AddRabbitListener<TEvent, THandler>(queue, idempotentConsumer)` + `IInboxGuard` |
| `RabbitTopology.consumerQueue(...)` | `services.AddConsumerQueue(queue, patterns)` (kèm `<queue>.dlq`) |
| `@Scheduled` + ShedLock | `IScheduledJob` + `JobLockProvider` (cùng bảng/giao thức `shared.shedlock`) |
| `AttachmentService` (MinIO) | `IAttachmentService`, `IAttachmentAccessPolicy`, Minio SDK |
| Actuator + Micrometer | `/actuator/health` (db, rabbit, minio), `/actuator/prometheus` (prometheus-net) |

## Map Java → .NET (identity, F01)

| Spring (`com.portflow.identity`) | .NET (`PortFlow.Identity`) |
|---|---|
| `oauth2ResourceServer().jwt()` + `NimbusJwtDecoder` (JWKS, iss, aud, skew 30s) | JwtBearer + `ConfigurationManager` đọc JWKS (`JwtAuthentication`) |
| `PortflowJwtAuthenticationConverter` → `TokenCurrentUser` | `JwtBearerEvents.OnTokenValidated` → `CurrentUserResolver` (JIT + cache 60s) |
| `RestAuthenticationEntryPoint` / `RestAccessDeniedHandler` | `OnChallenge` / `ProblemAuthorizationResultHandler` (401/403 ProblemDetail, `AUTH_SOD_VIOLATION`) |
| `@PreAuthorize("hasAuthority('X')")` | `[RequirePermission(Permission.X)]` (policy `perm:X`) |
| `hasPermission(#id, 'CONTRACT', 'read')` + `ScopeResolver` | `[RequireScope("CONTRACT")]` + `IScopeResolver` (filter sau permission) |
| `anyRequest().authenticated()`, public health/webhooks | Fallback policy = đã đăng nhập; `[AllowAnonymous]` cho health |
| `SecurityContextCurrentUserProvider` | `HttpContextCurrentUserProvider` (ngoài HTTP = `SystemUser`) |
| `Permission` enum | `Permission` hằng chuỗi (`Permission.CONTRACT_READ`) |
| JpaRepository (`AppUser`, `Role`, `Department`, `DocumentType`, `Delegation`) | `IdentityDbContext` (EF Core) |
| `UserDirectoryJdbc`, `IdentityQueries` (`JdbcClient`) | `UserDirectory`, `IdentityQueries` (Dapper) |
| Caffeine (`RolePermissionCache`, `CurrentUserCache`) | `MemoryCache` |
| `KeycloakAdminClient` (`RestClient`) + `KeycloakUserSyncJob` (ShedLock) | `KeycloakAdminClient` (`HttpClient`) + `IScheduledJob` |
| `@WithPortflowUser` / `TestJwtIssuer` (Nimbus) | `TestJwtIssuer` (ký RS256 + JWKS handler giả) / `KeycloakFixture` (Keycloak thật) |

## Map Java → .NET (workflow, F03)

| Spring (`com.portflow.workflow`) | .NET (`PortFlow.Workflow`) |
|---|---|
| `ApprovableDocumentHandler` (bean của module hồ sơ) | `IApprovableDocumentHandler` (đăng ký DI; `HandlerRegistry` tra theo loại hồ sơ) |
| `WorkflowService` / `WorkflowEngine` (`@Transactional`) | `IWorkflowService` / `WorkflowEngine` (`ITransactionRunner`) |
| JPA `WorkflowDefinition`, `WorkflowInstance` (+ `StepInstance`, `ApprovalAction`) | `WorkflowDbContext` (EF Core, `jsonb`, version `long`) |
| `SpelConditionEvaluator` (`SimpleEvaluationContext`) | `SpelConditionEvaluator` – parser riêng cho tập con SpEL (so sánh, `and/or/not`, số học, `matches`, `?:`), biến whitelist |
| `@stepAuth.canAct(#stepId)` trong `@PreAuthorize` | `[TypeFilter(StepActorFilter)]` → `IStepAuthorization` (403 + audit `ActionDenied` ở transaction riêng) |
| `InboxQueryService` (`JdbcClient`) | `InboxQueryService` (Dapper) |
| `SlaScanner` (`@Scheduled` + ShedLock) | `SlaScanner` (`IScheduledJob`, cron `0 */5 * * * *`) |
| `@ApplicationModuleListener UserDeactivated` | `IDomainEventListener<UserDeactivated>` |
| `TestApprovableDocuments` / `RecordingDocumentHandler` | `Workflow/Testing/TestApprovableDocuments.cs` (`TEST_DOC`, `TEST_DOC_ALL`) |

## Khác biệt cần biết

- `Money` kiểm tra mã tiền tệ theo định dạng `^[A-Z]{3}$` (Java dùng `Currency.getInstance`).
- `DateRange.Intersect` trả `null` thay cho `Optional.empty()`.
- `event_publication.event_type`/`listener_id` lưu tên kiểu .NET, nên bản Java và bản .NET không resubmit được event của nhau.
- Message RabbitMQ có thêm `message_id`, `correlation_id`, header `eventType`, `x-occurred-at` (Java chưa gắn được).
- API module là async (`Task<...>`) và trả `null` thay cho `Optional`.
- `GET /api/v1/users/lookup` thiếu `role` → 400 `SYS_BAD_REQUEST` như Java; lỗi model binding khác → 422.
- Endpoint F00 đã có phân quyền như Java: outbox cần `System.Admin`, đính kèm cần đăng nhập, `/actuator/prometheus` cần đăng nhập.
- Chưa có module hồ sơ nào (Contract, Pricing…) đăng ký `IApprovableDocumentHandler`, nên chưa trình duyệt được hồ sơ thật;
  kích hoạt định nghĩa cho loại hồ sơ chưa có handler sẽ báo lỗi V7.
- **Chỉ dev (không có bên Java):** `Portflow:Workflow:Demo:Enabled=true` (bật sẵn trong `compose.yaml` qua
  `WORKFLOW_DEMO_ENABLED`, và profile `dev`) đăng ký loại hồ sơ `DEMO` + `POST /api/v1/dev/workflow-demo` để thử luồng
  duyệt F03 trên UI. Tắt mặc định; khi tắt endpoint trả 404. Bỏ đi khi đã port module hồ sơ thật.
