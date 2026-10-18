-- F04 Contract & Addendum (schema contract). Gộp contract_init + contract_addendum của spec.
-- Khác spec: currency dùng text + CHECK (thay char(3)) để khớp kiểu String của JPA (ddl-auto=validate).
CREATE SCHEMA IF NOT EXISTS contract;

-- Sinh số HĐ: HD-2026-0001; số phụ lục: <số HĐ>/PL-01
CREATE TABLE contract.number_sequence (
    kind        text NOT NULL,          -- 'CONTRACT'
    year        int  NOT NULL,
    last_value  int  NOT NULL DEFAULT 0,
    PRIMARY KEY (kind, year)
);

CREATE TABLE contract.contract (
    id                      uuid PRIMARY KEY,
    number                  text          NOT NULL,
    customer_id             uuid          NOT NULL,          -- logic ref customer.customer
    title                   text          NOT NULL,
    effective_from          date          NOT NULL,
    effective_to            date          NOT NULL,
    value                   numeric(18,2) NOT NULL,
    currency                text          NOT NULL DEFAULT 'VND',
    payment_days            int           NOT NULL,
    payment_method          text          NOT NULL,
    payment_note            text,
    service_terms           text,
    status                  text          NOT NULL DEFAULT 'DRAFT',
    revision                int           NOT NULL DEFAULT 0,
    requires_esign          boolean       NOT NULL,
    signing_state           text          NOT NULL DEFAULT 'NOT_REQUIRED',
    supersedes_contract_id  uuid          REFERENCES contract.contract(id),
    owner_department_id     uuid          NOT NULL,
    submitter_id            uuid,
    approved_at             timestamptz,
    activated_at            timestamptz,
    cancel_reason           text,
    created_at              timestamptz   NOT NULL DEFAULT now(),
    created_by              uuid          NOT NULL,
    updated_at              timestamptz   NOT NULL DEFAULT now(),
    updated_by              uuid          NOT NULL,
    version                 bigint        NOT NULL DEFAULT 0,
    CONSTRAINT uq_contract_number UNIQUE (number),
    CONSTRAINT ck_contract_dates  CHECK (effective_from <= effective_to),          -- CTR-02
    CONSTRAINT ck_contract_value  CHECK (value >= 0),
    CONSTRAINT ck_contract_currency CHECK (currency IN ('VND','USD')),
    CONSTRAINT ck_contract_pay    CHECK (payment_days BETWEEN 0 AND 365),
    CONSTRAINT ck_contract_status CHECK (status IN ('DRAFT','SUBMITTED','UNDER_REVIEW','REVISION_REQUESTED',
                                                    'APPROVED','ACTIVE','EXPIRED','REJECTED','CANCELLED')),
    CONSTRAINT ck_signing_state   CHECK (signing_state IN ('NOT_REQUIRED','PENDING','IN_PROGRESS','SIGNED','FAILED')),
    CONSTRAINT ck_cancel_reason   CHECK (status <> 'CANCELLED' OR cancel_reason IS NOT NULL),
    CONSTRAINT ck_not_self_supersede CHECK (supersedes_contract_id IS NULL OR supersedes_contract_id <> id)
);
CREATE INDEX ix_contract_customer      ON contract.contract (customer_id, status);
CREATE INDEX ix_contract_dept_status   ON contract.contract (owner_department_id, status, updated_at DESC);
-- Job: tìm nhanh hồ sơ đến hạn
CREATE INDEX ix_contract_due_activate  ON contract.contract (effective_from) WHERE status = 'APPROVED';
CREATE INDEX ix_contract_due_expire    ON contract.contract (effective_to)   WHERE status = 'ACTIVE';

CREATE TABLE contract.contract_service (
    id                  uuid PRIMARY KEY,
    contract_id         uuid    NOT NULL REFERENCES contract.contract(id) ON DELETE CASCADE,
    service_code        text    NOT NULL,           -- logic ref pricing.service.code
    mandatory           boolean NOT NULL DEFAULT false,
    valid_from          date,                       -- null = từ đầu HĐ; set khi SERVICE_ADDITION
    source_addendum_id  uuid,
    CONSTRAINT uq_contract_service UNIQUE (contract_id, service_code)
);

-- Điều khoản theo thời điểm (FR-A03). Dòng gốc tạo khi HĐ Approved; mỗi phụ lục APPLIED thêm 1 dòng.
CREATE TABLE contract.contract_terms (
    id                  uuid PRIMARY KEY,
    contract_id         uuid    NOT NULL REFERENCES contract.contract(id),
    valid_from          date    NOT NULL,
    effective_to        date    NOT NULL,
    payment_days        int     NOT NULL,
    payment_method      text    NOT NULL,
    payment_note        text,
    service_terms       text,
    source_addendum_id  uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_contract_terms_from UNIQUE (contract_id, valid_from, source_addendum_id)
);
CREATE INDEX ix_contract_terms_lookup ON contract.contract_terms (contract_id, valid_from DESC);

-- Snapshot mỗi lần submit (FR-K06)
CREATE TABLE contract.contract_version (
    id            uuid PRIMARY KEY,
    contract_id   uuid        NOT NULL REFERENCES contract.contract(id) ON DELETE CASCADE,
    revision      int         NOT NULL,
    snapshot      jsonb       NOT NULL,
    submitted_at  timestamptz NOT NULL,
    submitted_by  uuid        NOT NULL,
    CONSTRAINT uq_contract_version UNIQUE (contract_id, revision)
);

CREATE TABLE contract.addendum (
    id              uuid PRIMARY KEY,
    contract_id     uuid        NOT NULL REFERENCES contract.contract(id),
    number          text        NOT NULL,
    seq             int         NOT NULL,
    type            text        NOT NULL,
    effective_from  date        NOT NULL,
    changes         jsonb       NOT NULL,
    status          text        NOT NULL DEFAULT 'DRAFT',
    revision        int         NOT NULL DEFAULT 0,
    submitter_id    uuid,
    applied_at      timestamptz,
    cancel_reason   text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid        NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      uuid        NOT NULL,
    version         bigint      NOT NULL DEFAULT 0,
    CONSTRAINT uq_addendum_number UNIQUE (number),
    CONSTRAINT uq_addendum_seq    UNIQUE (contract_id, seq),
    CONSTRAINT ck_addendum_type   CHECK (type IN ('PRICE_CHANGE','EXTENSION','PAYMENT_TERMS','SERVICE_ADDITION')),
    CONSTRAINT ck_addendum_status CHECK (status IN ('DRAFT','SUBMITTED','UNDER_REVIEW','REVISION_REQUESTED',
                                                    'APPROVED','APPLIED','REJECTED','CANCELLED')),
    CONSTRAINT ck_addendum_changes CHECK (jsonb_typeof(changes) = 'object')
);
-- ADD-R2: không cho 2 phụ lục EXTENSION đang chờ/đã duyệt nhưng chưa áp dụng cho cùng HĐ
CREATE UNIQUE INDEX uq_addendum_pending_extension ON contract.addendum (contract_id)
    WHERE type = 'EXTENSION' AND status IN ('SUBMITTED','UNDER_REVIEW','REVISION_REQUESTED','APPROVED');
CREATE INDEX ix_addendum_due ON contract.addendum (effective_from) WHERE status = 'APPROVED';
