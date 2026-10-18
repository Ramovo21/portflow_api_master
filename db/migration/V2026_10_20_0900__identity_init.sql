-- F01 Identity: bản sao người dùng từ Keycloak + dữ liệu phân quyền của app (schema identity)
CREATE SCHEMA IF NOT EXISTS identity;
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE identity.department (
    id           uuid PRIMARY KEY,
    code         text NOT NULL,                 -- khớp attribute 'department' trong Keycloak
    name         text NOT NULL,
    head_user_id uuid,
    parent_id    uuid REFERENCES identity.department(id),
    created_at   timestamptz NOT NULL DEFAULT now(),
    created_by   uuid,
    updated_at   timestamptz,
    updated_by   uuid,
    version      bigint NOT NULL DEFAULT 0,
    CONSTRAINT ux_department_code UNIQUE (code)
);

-- Bản sao người dùng từ Keycloak. id = claim 'sub'.
CREATE TABLE identity.app_user (
    id            uuid PRIMARY KEY,
    username      text NOT NULL,
    full_name     text NOT NULL,
    email         text,
    department_id uuid REFERENCES identity.department(id),   -- null = Unprovisioned
    active        boolean NOT NULL DEFAULT true,
    sod_violation boolean NOT NULL DEFAULT false,
    synced_at     timestamptz NOT NULL DEFAULT now(),
    sync_source   text NOT NULL DEFAULT 'JIT',                -- JIT | JOB | MANUAL | SEED
    last_seen_at  timestamptz,
    version       bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_app_user_sync_source CHECK (sync_source IN ('JIT', 'JOB', 'MANUAL', 'SEED'))
);
CREATE UNIQUE INDEX ux_user_username ON identity.app_user (lower(username));
CREATE INDEX ix_user_department ON identity.app_user (department_id) WHERE active;

ALTER TABLE identity.department
    ADD CONSTRAINT fk_department_head FOREIGN KEY (head_user_id) REFERENCES identity.app_user(id);

CREATE TABLE identity.role (
    code        text PRIMARY KEY,               -- = tên realm role trong Keycloak
    name        text NOT NULL,
    permissions text[] NOT NULL DEFAULT '{}',
    data_scope  text NOT NULL DEFAULT 'ALL',
    system      boolean NOT NULL DEFAULT false,
    version     bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_role_data_scope CHECK (data_scope IN ('DEPARTMENT', 'ALL'))
);

-- Bản sao role mapping (nguồn: Keycloak). Role lạ (không có trong bảng role) bị bỏ qua khi sync.
CREATE TABLE identity.user_role (
    user_id   uuid NOT NULL REFERENCES identity.app_user(id) ON DELETE CASCADE,
    role_code text NOT NULL REFERENCES identity.role(code),
    PRIMARY KEY (user_id, role_code)
);
CREATE INDEX ix_user_role_role ON identity.user_role (role_code);

CREATE TABLE identity.document_type (
    code                text PRIMARY KEY,      -- CONTRACT | ADDENDUM | PRICE_LIST | PAYMENT_SHEET
    name                text NOT NULL,
    requires_esign      boolean NOT NULL DEFAULT false,
    workflow_key        text NOT NULL,
    active              boolean NOT NULL DEFAULT true,
    submit_requirements jsonb NOT NULL DEFAULT '{}'::jsonb,
    version             bigint NOT NULL DEFAULT 0
);

-- COULD (FR-W04): ủy quyền có thời hạn. document_types rỗng = mọi loại hồ sơ.
CREATE TABLE identity.delegation (
    id             uuid PRIMARY KEY,
    from_user_id   uuid NOT NULL REFERENCES identity.app_user(id),
    to_user_id     uuid NOT NULL REFERENCES identity.app_user(id),
    document_types text[] NOT NULL DEFAULT '{}',
    valid_from     date NOT NULL,
    valid_to       date NOT NULL,
    reason         text NOT NULL,
    revoked        boolean NOT NULL DEFAULT false,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_delegation_not_self CHECK (from_user_id <> to_user_id),
    CONSTRAINT ck_delegation_range CHECK (valid_from <= valid_to),
    CONSTRAINT delegation_no_overlap EXCLUDE USING gist (
        from_user_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&) WHERE (NOT revoked)
);
CREATE INDEX ix_delegation_to ON identity.delegation (to_user_id) WHERE NOT revoked;

-- Dữ liệu tham chiếu (mọi môi trường): role → permission (F01 §8.3) và loại hồ sơ.
-- Role 'system' không sửa được qua API; ADMIN không có Workflow.Act (SoD).
INSERT INTO identity.role (code, name, permissions, data_scope, system) VALUES
    ('SALES', 'Nhân viên Kinh doanh', ARRAY[
        'Customer.Read', 'Customer.Create', 'Customer.Update',
        'Contract.Read', 'Contract.Create', 'Contract.Update', 'Contract.Submit', 'Contract.Clone',
        'Addendum.Create', 'Addendum.Submit',
        'PriceList.Read', 'PriceList.Create', 'PriceList.Submit',
        'Workflow.Act', 'PaymentSheet.Read', 'ESign.Read', 'Audit.Read', 'AI.Use'], 'DEPARTMENT', true),
    ('SALES_MANAGER', 'Trưởng phòng Kinh doanh', ARRAY[
        'Customer.Read', 'Customer.Create', 'Customer.Update', 'Customer.Suspend',
        'Contract.Read', 'Contract.Create', 'Contract.Update', 'Contract.Submit', 'Contract.Cancel', 'Contract.Clone',
        'Addendum.Create', 'Addendum.Submit',
        'PriceList.Read', 'PriceList.Create', 'PriceList.Submit',
        'Workflow.Act', 'PaymentSheet.Read', 'ESign.Read', 'Audit.Read', 'AI.Use'], 'DEPARTMENT', true),
    ('OPERATIONS', 'Nhân viên Khai thác', ARRAY[
        'Volume.Read', 'Volume.Record', 'Volume.Lock', 'Volume.Import',
        'Contract.Read', 'Workflow.Act', 'AI.Use'], 'ALL', true),
    ('OPERATIONS_LEAD', 'Trưởng phòng Khai thác', ARRAY[
        'Volume.Read', 'Volume.Record', 'Volume.Lock', 'Volume.Import', 'Volume.ReopenPeriod',
        'Contract.Read', 'Workflow.Act', 'AI.Use'], 'ALL', true),
    ('ACCOUNTANT', 'Kế toán', ARRAY[
        'PaymentSheet.Read', 'PaymentSheet.Create', 'PaymentSheet.Reconcile', 'PaymentSheet.Submit',
        'PaymentSheet.SendESign', 'PaymentSheet.Publish', 'PaymentSheet.Adjust',
        'Volume.Read', 'PriceList.Read', 'Contract.Read',
        'ESign.Read', 'ESign.Cancel', 'ESign.Retry', 'Workflow.Act', 'AI.Use'], 'ALL', true),
    ('CHIEF_ACCOUNTANT', 'Kế toán trưởng', ARRAY[
        'PaymentSheet.Read', 'PaymentSheet.Create', 'PaymentSheet.Reconcile', 'PaymentSheet.Submit',
        'PaymentSheet.SendESign', 'PaymentSheet.Publish', 'PaymentSheet.Adjust',
        'Volume.Read', 'PriceList.Read', 'Contract.Read',
        'ESign.Read', 'ESign.Cancel', 'ESign.Retry', 'Workflow.Act', 'AI.Use'], 'ALL', true),
    ('LEGAL', 'Pháp chế', ARRAY[
        'Contract.Read', 'Workflow.Act', 'Audit.Read', 'AI.Use'], 'ALL', true),
    ('DIRECTOR', 'Ban Giám đốc', ARRAY[
        'Customer.Read', 'Contract.Read', 'PriceList.Read', 'Volume.Read', 'PaymentSheet.Read',
        'ESign.Read', 'Audit.ReadAll', 'Workflow.Act', 'AI.Use', 'Delegation.Manage'], 'ALL', true),
    ('ADMIN', 'Quản trị', ARRAY[
        'User.Admin', 'Role.Admin', 'DocumentType.Admin', 'Workflow.Admin', 'System.Admin',
        'Audit.ReadAll', 'Delegation.Manage'], 'ALL', true);

INSERT INTO identity.document_type (code, name, requires_esign, workflow_key, active, submit_requirements) VALUES
    ('CONTRACT', 'Hợp đồng', true, 'CONTRACT', true, '{}'::jsonb),
    ('ADDENDUM', 'Phụ lục hợp đồng', false, 'ADDENDUM', true, '{}'::jsonb),
    ('PRICE_LIST', 'Bảng giá', false, 'PRICE_LIST', true, '{}'::jsonb),
    ('PAYMENT_SHEET', 'Bảng thanh toán', true, 'PAYMENT_SHEET', true, '{}'::jsonb);
