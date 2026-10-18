-- F00 Foundation: hạ tầng dùng chung (schema shared)
CREATE SCHEMA IF NOT EXISTS shared;
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- dùng bởi pricing (PRC-03), delegation
CREATE EXTENSION IF NOT EXISTS pgcrypto;     -- digest() cho audit hash-chain (F10)

-- QĐ-06: Idempotency-Key, ghi cùng transaction với nghiệp vụ
CREATE TABLE shared.idempotency_request (
    key             text        NOT NULL,
    user_id         uuid        NOT NULL,
    method          text        NOT NULL,
    path            text        NOT NULL,
    request_hash    text        NOT NULL,           -- SHA-256(method|path|args)
    status          text        NOT NULL DEFAULT 'IN_PROGRESS',
    response_status int,
    response_body   text,                           -- JSON gốc; text (không jsonb) để phát lại đúng từng byte
    created_at      timestamptz NOT NULL DEFAULT now(),
    completed_at    timestamptz,
    PRIMARY KEY (key, user_id),
    CONSTRAINT ck_idem_status CHECK (status IN ('IN_PROGRESS', 'COMPLETED')),
    CONSTRAINT ck_idem_completed CHECK (status = 'IN_PROGRESS' OR response_status IS NOT NULL)
);
CREATE INDEX ix_idem_created ON shared.idempotency_request (created_at);

-- QĐ-07: inbox cho consumer idempotent
CREATE TABLE shared.inbox_message (
    message_id   text        NOT NULL,
    consumer     text        NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (message_id, consumer)
);
CREATE INDEX ix_inbox_processed ON shared.inbox_message (processed_at);

-- QĐ-07: outbox = Event Publication Registry của Spring Modulith 2.1 (schema v2 chính thức, đặt trong schema shared)
CREATE TABLE shared.event_publication (
    id                     uuid        NOT NULL,
    listener_id            text        NOT NULL,
    event_type             text        NOT NULL,
    serialized_event       text        NOT NULL,
    publication_date       timestamptz NOT NULL,
    completion_date        timestamptz,
    status                 text,
    completion_attempts    int,
    last_resubmission_date timestamptz,
    PRIMARY KEY (id)
);
CREATE INDEX event_publication_serialized_event_hash_idx ON shared.event_publication USING hash (serialized_event);
CREATE INDEX event_publication_by_completion_date_idx ON shared.event_publication (completion_date);

-- Tệp đính kèm dùng chung (contract, billing, esign)
CREATE TABLE shared.attachment (
    id           uuid        PRIMARY KEY,
    entity_type  text        NOT NULL,
    entity_id    uuid        NOT NULL,
    file_name    text        NOT NULL,
    content_type text        NOT NULL,
    storage_key  text        NOT NULL UNIQUE,
    sha256       char(64)    NOT NULL,
    size         bigint      NOT NULL,
    uploaded_by  uuid        NOT NULL,
    uploaded_at  timestamptz NOT NULL DEFAULT now(),
    locked       boolean     NOT NULL DEFAULT false,   -- true khi hồ sơ đã duyệt/ký
    CONSTRAINT ck_attachment_size CHECK (size > 0 AND size <= 26214400)   -- 25 MB
);
CREATE INDEX ix_attachment_entity ON shared.attachment (entity_type, entity_id);

-- ShedLock cho scheduler (F11) và job hạ tầng
CREATE TABLE shared.shedlock (
    name       varchar(64)  PRIMARY KEY,
    lock_until timestamptz  NOT NULL,
    locked_at  timestamptz  NOT NULL,
    locked_by  varchar(255) NOT NULL
);
