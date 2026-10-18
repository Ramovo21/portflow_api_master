-- F05 §4.2 Bảng giá. Khác spec: currency text + CHECK (thay char(3)); thêm owner_department_id (phạm vi phòng ban §9).
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE SEQUENCE pricing.price_list_code_seq START 1;

CREATE TABLE pricing.price_list (
    id                   uuid PRIMARY KEY,
    code                 text NOT NULL UNIQUE,                 -- BG-2026-000123, sinh ở app
    scope                text NOT NULL CHECK (scope IN ('CONTRACT','CUSTOMER','SERVICE_GROUP','GLOBAL')),
    scope_id             uuid,
    -- NULL không bằng NULL trong EXCLUDE → dùng cột sinh ra với sentinel để GLOBAL cũng bị kiểm tra chồng lấn.
    scope_key            uuid GENERATED ALWAYS AS (COALESCE(scope_id, '00000000-0000-0000-0000-000000000000'::uuid)) STORED,
    service_type         text NOT NULL,                        -- service_group.code hoặc '*'
    name                 text NOT NULL,
    list_version         int  NOT NULL CHECK (list_version >= 1),
    revision_number      int  NOT NULL DEFAULT 0,
    currency             text NOT NULL DEFAULT 'VND' CHECK (currency IN ('VND','USD')),
    status               text NOT NULL CHECK (status IN ('DRAFT','SUBMITTED','APPROVED','EFFECTIVE','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    valid_from           date NOT NULL,
    valid_to             date,                                  -- NULL = vô thời hạn
    used_in_billing      boolean NOT NULL DEFAULT false,
    billed_through       date,                                  -- ngày cuối cùng đã dùng để lập bảng thanh toán
    previous_version_id  uuid REFERENCES pricing.price_list(id),
    superseded_by_id     uuid REFERENCES pricing.price_list(id),
    source_addendum_id   uuid,                                  -- logic ref contract.addendum
    workflow_instance_id uuid,
    owner_department_id  uuid,                                  -- logic ref identity.department
    note                 text,
    created_at           timestamptz NOT NULL DEFAULT now(),
    created_by           uuid,
    updated_at           timestamptz,
    updated_by           uuid,
    version              bigint NOT NULL DEFAULT 0,

    CONSTRAINT ck_pl_range   CHECK (valid_to IS NULL OR valid_from <= valid_to),                       -- PRC-02
    CONSTRAINT ck_pl_scope   CHECK ((scope = 'GLOBAL') = (scope_id IS NULL)),                          -- PRC-01
    CONSTRAINT ck_pl_global  CHECK (scope <> 'GLOBAL' OR service_type = '*'),
    CONSTRAINT ck_pl_billed  CHECK (billed_through IS NULL OR used_in_billing),
    CONSTRAINT uq_pl_version UNIQUE (scope, scope_key, service_type, list_version),   -- listVersion tăng dần theo chuỗi
    -- PRC-03 (QĐ-08): chống chồng lấn hiệu lực ở mức DB. DEFERRABLE: supersede trong 1 transaction không phụ thuộc
    -- thứ tự flush; app ép kiểm ngay bằng SET CONSTRAINTS ... IMMEDIATE sau khi duyệt.
    CONSTRAINT ex_pl_overlap EXCLUDE USING gist (
        scope        WITH =,
        scope_key    WITH =,
        service_type WITH =,
        daterange(valid_from, valid_to, '[]') WITH &&
    ) WHERE (status IN ('APPROVED','EFFECTIVE','SUPERSEDED','EXPIRED'))
      DEFERRABLE INITIALLY DEFERRED
);

-- Một chuỗi chỉ có tối đa 1 version mới đang "trên đường duyệt"
CREATE UNIQUE INDEX uq_pl_one_pending ON pricing.price_list (scope, scope_key, service_type)
    WHERE status IN ('DRAFT','SUBMITTED') AND previous_version_id IS NOT NULL;

CREATE INDEX ix_pl_resolve ON pricing.price_list (scope, scope_key, service_type, valid_from)
    WHERE status IN ('EFFECTIVE','SUPERSEDED','EXPIRED');
CREATE INDEX ix_pl_lifecycle ON pricing.price_list (status, valid_from) WHERE status IN ('APPROVED','EFFECTIVE');
CREATE INDEX ix_pl_scope_lookup ON pricing.price_list (scope_id) WHERE scope_id IS NOT NULL;
CREATE INDEX ix_pl_source_addendum ON pricing.price_list (source_addendum_id) WHERE source_addendum_id IS NOT NULL;

CREATE TABLE pricing.price_list_line (
    id             uuid PRIMARY KEY,
    price_list_id  uuid NOT NULL REFERENCES pricing.price_list(id) ON DELETE CASCADE,
    service_code   text NOT NULL,
    unit_price     numeric(18,2) NOT NULL CHECK (unit_price >= 0),
    uom            text NOT NULL,
    min_qty        numeric(18,3) NOT NULL DEFAULT 0 CHECK (min_qty >= 0),
    note           text,
    CONSTRAINT uq_pll_service UNIQUE (price_list_id, service_code)
);
CREATE INDEX ix_pll_service ON pricing.price_list_line (service_code);
