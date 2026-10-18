-- F02 Customer (schema customer)
CREATE SCHEMA IF NOT EXISTS customer;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE SEQUENCE customer.customer_code_seq START 1;

CREATE TABLE customer.customer (
    id                   uuid PRIMARY KEY,
    code                 text        NOT NULL,
    name                 text        NOT NULL CHECK (length(trim(name)) > 0),
    tax_code             text        NOT NULL,
    address              text,
    representative       jsonb       NOT NULL DEFAULT '{}'::jsonb,
    contacts             jsonb       NOT NULL DEFAULT '[]'::jsonb,
    status               text        NOT NULL DEFAULT 'ACTIVE',
    status_reason        text,
    owner_department_id  uuid        NOT NULL,        -- logic ref identity.department (không FK xuyên schema)
    created_at           timestamptz NOT NULL DEFAULT now(),
    created_by           uuid        NOT NULL,
    updated_at           timestamptz NOT NULL DEFAULT now(),
    updated_by           uuid        NOT NULL,
    version              bigint      NOT NULL DEFAULT 0,
    CONSTRAINT ck_customer_status   CHECK (status IN ('ACTIVE','SUSPENDED')),
    CONSTRAINT ck_customer_taxcode  CHECK (tax_code ~ '^[0-9]{10}(-[0-9]{3})?$'),   -- đã chuẩn hóa
    CONSTRAINT ck_customer_contacts CHECK (jsonb_typeof(contacts) = 'array'),
    CONSTRAINT ck_suspend_reason    CHECK (status = 'ACTIVE' OR status_reason IS NOT NULL),
    CONSTRAINT uq_customer_code     UNIQUE (code),
    CONSTRAINT uq_customer_taxcode  UNIQUE (tax_code)                               -- FR-C03
);

-- Tìm kiếm theo tên/mã/MST (ILIKE '%...%')
CREATE INDEX ix_customer_name_trgm ON customer.customer USING gin (name gin_trgm_ops);
CREATE INDEX ix_customer_status_dept ON customer.customer (owner_department_id, status, created_at DESC);
