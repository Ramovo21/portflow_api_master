-- Chạy một lần khi volume postgres còn trống. Volume cũ: tạo tay bằng
--   docker compose exec postgres psql -U portflow -c "CREATE DATABASE keycloak"
CREATE DATABASE keycloak;
