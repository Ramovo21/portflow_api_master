-- F05 §4.1 Danh mục dịch vụ (schema pricing) + dữ liệu tham chiếu (F05-T02: 4 nhóm, 13 dịch vụ) cho mọi môi trường.
CREATE SCHEMA IF NOT EXISTS pricing;

CREATE TABLE pricing.service_group (
    id          uuid PRIMARY KEY,
    code        text NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_]{2,40}$'),
    name        text NOT NULL,
    active      boolean NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid,
    updated_at  timestamptz,
    updated_by  uuid,
    version     bigint NOT NULL DEFAULT 0
);

CREATE TABLE pricing.service (
    id                uuid PRIMARY KEY,
    code              text NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_-]{2,40}$'),
    name              text NOT NULL,
    group_id          uuid NOT NULL REFERENCES pricing.service_group(id),
    uom               text NOT NULL CHECK (uom IN ('CONT20','CONT40','CONT45','TEU','TON','TRIP','DAY','CONT_DAY','SET')),
    default_tax_rate  numeric(5,4) NOT NULL DEFAULT 0.08 CHECK (default_tax_rate >= 0 AND default_tax_rate < 1),
    mandatory_default boolean NOT NULL DEFAULT false,
    active            boolean NOT NULL DEFAULT true,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid,
    updated_at        timestamptz,
    updated_by        uuid,
    version           bigint NOT NULL DEFAULT 0
);
CREATE INDEX ix_service_group ON pricing.service(group_id) WHERE active;

INSERT INTO pricing.service_group (id, code, name) VALUES
    ('0192f0d0-0000-7000-8000-000000000001', 'CONT_HANDLING', 'Xếp dỡ container'),
    ('0192f0d0-0000-7000-8000-000000000002', 'STORAGE', 'Lưu bãi'),
    ('0192f0d0-0000-7000-8000-000000000003', 'TRUCKING', 'Vận chuyển'),
    ('0192f0d0-0000-7000-8000-000000000004', 'REEFER', 'Dịch vụ container lạnh');

INSERT INTO pricing.service (id, code, name, group_id, uom, mandatory_default) VALUES
    ('0192f0d1-0000-7000-8000-000000000001', 'CONT20_HANDLING', 'Xếp dỡ cont 20''', '0192f0d0-0000-7000-8000-000000000001', 'CONT20', true),
    ('0192f0d1-0000-7000-8000-000000000002', 'CONT40_HANDLING', 'Xếp dỡ cont 40''', '0192f0d0-0000-7000-8000-000000000001', 'CONT40', true),
    ('0192f0d1-0000-7000-8000-000000000003', 'LIFT-CONT20', 'Nâng hạ cont 20''', '0192f0d0-0000-7000-8000-000000000001', 'CONT20', false),
    ('0192f0d1-0000-7000-8000-000000000004', 'LIFT-CONT40', 'Nâng hạ cont 40''', '0192f0d0-0000-7000-8000-000000000001', 'CONT40', false),
    ('0192f0d1-0000-7000-8000-000000000005', 'LIFT-CONT45', 'Nâng hạ cont 45''', '0192f0d0-0000-7000-8000-000000000001', 'CONT45', false),
    ('0192f0d1-0000-7000-8000-000000000006', 'STORAGE_DAY', 'Lưu bãi cont/ngày (chung)', '0192f0d0-0000-7000-8000-000000000002', 'CONT_DAY', false),
    ('0192f0d1-0000-7000-8000-000000000007', 'STO-CONT-DAY', 'Lưu bãi cont khô/ngày', '0192f0d0-0000-7000-8000-000000000002', 'CONT_DAY', false),
    ('0192f0d1-0000-7000-8000-000000000008', 'STO-REEFER-DAY', 'Lưu bãi cont lạnh/ngày', '0192f0d0-0000-7000-8000-000000000002', 'CONT_DAY', false),
    ('0192f0d1-0000-7000-8000-000000000009', 'STO-DG-DAY', 'Lưu bãi hàng nguy hiểm/ngày', '0192f0d0-0000-7000-8000-000000000002', 'CONT_DAY', false),
    ('0192f0d1-0000-7000-8000-000000000010', 'TRUCK-TRIP', 'Vận chuyển nội cảng/chuyến', '0192f0d0-0000-7000-8000-000000000003', 'TRIP', false),
    ('0192f0d1-0000-7000-8000-000000000011', 'TRUCK-TON', 'Vận chuyển hàng rời/tấn', '0192f0d0-0000-7000-8000-000000000003', 'TON', false),
    ('0192f0d1-0000-7000-8000-000000000012', 'REEFER_PLUGIN', 'Cắm điện cont lạnh/ngày', '0192f0d0-0000-7000-8000-000000000004', 'DAY', false),
    ('0192f0d1-0000-7000-8000-000000000013', 'REEFER-PTI', 'Kiểm tra PTI cont lạnh', '0192f0d0-0000-7000-8000-000000000004', 'SET', false);
