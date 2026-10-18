# Keycloak – realm `portflow` (F01)

`portflow-realm.json` được container `keycloak` (profile `auth`) import khi khởi động
(`docker compose --profile auth up -d`). Console: http://localhost:8180/admin (admin / admin).

| Client | Dùng cho |
|---|---|
| `portflow-web` | SPA – Authorization Code + PKCE S256, redirect `http://localhost:5173/*` |
| `portflow-api` | Bearer-only, chỉ là **audience** (`aud: portflow-api`) |
| `portflow-backend` | Worker đồng bộ user (client credentials, service account có `view-users`, `query-users`). Secret dev: `portflow-backend-secret` |
| `portflow-dev-cli` | Postman / script test – password grant (**chỉ realm dev**) |

Token có claim `department` (mapper trên `portflow-web` và `portflow-dev-cli`, attribute khai báo trong user profile).
9 user mẫu (`sales01`, `salesmgr`, `ops01`, `acc01`, `chiefacc`, `legal01`, `director`, `director2`, `admin`),
mật khẩu `Portflow@123`, UUID trùng `src/main/resources/db/seed`.

## Lấy token trong 1 lệnh (dev)

```bash
TOKEN=$(curl -s -X POST http://localhost:8180/realms/portflow/protocol/openid-connect/token \
  -d grant_type=password -d client_id=portflow-dev-cli \
  -d username=salesmgr -d password='Portflow@123' | jq -r .access_token)
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/me | jq
```

Postman: tab *Authorization* → *OAuth 2.0*, Grant type *Password Credentials*,
Access Token URL `http://localhost:8180/realms/portflow/protocol/openid-connect/token`, Client ID `portflow-dev-cli`.

## Sửa cấu hình realm

Mọi thay đổi trong console phải **export lại** và commit (review như code):

```bash
docker compose exec keycloak /opt/keycloak/bin/kc.sh export --realm portflow --users realm_file \
  --file /tmp/portflow-realm.json && docker compose cp keycloak:/tmp/portflow-realm.json infra/keycloak/
```

`KeycloakLoginE2EIT` / `KeycloakSyncIT` (tag `keycloak`) chạy với đúng file này – sửa realm sai sẽ làm đỏ CI.

## 401 hàng loạt trong Docker?

`iss` của token là URL trình duyệt thấy (`http://localhost:8180/...`, nhờ `KC_HOSTNAME`), còn API tải JWKS qua
`http://keycloak:8080/...`. Kiểm tra `KC_ISSUER` / `KC_JWKS` của service `api`; bật
`logging.level.com.portflow.identity.infrastructure.security=DEBUG` để xem lý do token bị từ chối.
