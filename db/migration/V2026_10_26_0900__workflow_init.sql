-- F03 Workflow engine (schema workflow). Ủy quyền dùng identity.delegation (F01) – không có bảng riêng ở đây.
CREATE SCHEMA IF NOT EXISTS workflow;

-- ===== Definition =====
CREATE TABLE workflow.workflow_definition (
    id               uuid PRIMARY KEY,
    document_type    text        NOT NULL,
    version          int         NOT NULL,
    status           text        NOT NULL DEFAULT 'DRAFT',
    effective_from   date,
    definition       jsonb       NOT NULL,
    resubmit_policy  text        NOT NULL DEFAULT 'RESTART',
    activated_at     timestamptz,
    activated_by     uuid,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid,
    updated_at       timestamptz,
    updated_by       uuid,
    row_version      bigint      NOT NULL DEFAULT 0,
    CONSTRAINT ck_wd_status  CHECK (status IN ('DRAFT','ACTIVE','RETIRED')),
    CONSTRAINT ck_wd_resub   CHECK (resubmit_policy IN ('RESTART','RESUME_AT_REQUESTER')),
    CONSTRAINT ck_wd_version CHECK (version >= 1),
    CONSTRAINT ck_wd_steps   CHECK (jsonb_typeof(definition -> 'steps') = 'array'
                                    AND jsonb_array_length(definition -> 'steps') >= 1),
    CONSTRAINT uq_wd_type_version UNIQUE (document_type, version)
);
-- Mỗi loại hồ sơ đúng 1 definition ACTIVE
CREATE UNIQUE INDEX uq_wd_one_active ON workflow.workflow_definition (document_type)
    WHERE status = 'ACTIVE';

-- ===== Instance =====
CREATE TABLE workflow.workflow_instance (
    id                  uuid PRIMARY KEY,
    document_type       text        NOT NULL,
    document_id         uuid        NOT NULL,
    definition_id       uuid        NOT NULL REFERENCES workflow.workflow_definition(id),
    definition_version  int         NOT NULL,
    current_step_order  int,
    round               int         NOT NULL DEFAULT 1,
    status              text        NOT NULL,
    context_vars        jsonb       NOT NULL DEFAULT '{}'::jsonb,
    submitter_id        uuid        NOT NULL,
    submitted_at        timestamptz NOT NULL,
    completed_at        timestamptz,
    cancel_reason       text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,
    version             bigint      NOT NULL DEFAULT 0,      -- @Version
    CONSTRAINT ck_wi_status CHECK (status IN ('RUNNING','REVISION_REQUESTED','APPROVED','REJECTED','CANCELLED')),
    CONSTRAINT ck_wi_completed CHECK ((status IN ('APPROVED','REJECTED','CANCELLED')) = (completed_at IS NOT NULL)),
    CONSTRAINT ck_wi_current CHECK (status <> 'RUNNING' OR current_step_order IS NOT NULL)
);
-- Double submit: 1 hồ sơ chỉ có 1 instance đang chạy (FR-W03)
CREATE UNIQUE INDEX uq_wi_active_per_document ON workflow.workflow_instance (document_type, document_id)
    WHERE status IN ('RUNNING','REVISION_REQUESTED');
CREATE INDEX ix_wi_document  ON workflow.workflow_instance (document_type, document_id);
CREATE INDEX ix_wi_submitter ON workflow.workflow_instance (submitter_id, status, submitted_at DESC);
CREATE INDEX ix_wi_definition_active ON workflow.workflow_instance (definition_id)
    WHERE status IN ('RUNNING','REVISION_REQUESTED');

-- ===== Step =====
CREATE TABLE workflow.step_instance (
    id                   uuid PRIMARY KEY,
    instance_id          uuid        NOT NULL REFERENCES workflow.workflow_instance(id),
    step_order           int         NOT NULL,
    round                int         NOT NULL,
    name                 text        NOT NULL,
    assignee_user_ids    uuid[]      NOT NULL DEFAULT '{}',
    requires_all         boolean     NOT NULL DEFAULT false,
    on_reject            text        NOT NULL,
    status               text        NOT NULL,
    started_at           timestamptz,
    due_at               timestamptz,
    completed_at         timestamptz,
    acted_by             uuid,
    decision             text,
    comment              text,
    overdue_notified_at  timestamptz,
    CONSTRAINT ck_si_status   CHECK (status IN ('PENDING','COMPLETED','SKIPPED','CANCELLED')),
    CONSTRAINT ck_si_decision CHECK (decision IS NULL OR decision IN ('APPROVED','REJECTED','REVISION_REQUESTED')),
    CONSTRAINT ck_si_onreject CHECK (on_reject IN ('TERMINATE','BACK_TO_SUBMITTER')),
    CONSTRAINT ck_si_pending_has_assignee CHECK (status <> 'PENDING' OR cardinality(assignee_user_ids) > 0),
    CONSTRAINT ck_si_completed CHECK ((status = 'COMPLETED') = (decision IS NOT NULL)),
    CONSTRAINT uq_si_order_round UNIQUE (instance_id, round, step_order)        -- APR-02
);
-- Mỗi instance tối đa 1 step PENDING
CREATE UNIQUE INDEX uq_si_one_pending ON workflow.step_instance (instance_id) WHERE status = 'PENDING';
-- Inbox "cần tôi xử lý": GIN trên mảng assignee, chỉ cho bước đang chờ (NFR-06)
CREATE INDEX ix_si_assignees_pending ON workflow.step_instance USING gin (assignee_user_ids)
    WHERE status = 'PENDING';
CREATE INDEX ix_si_due ON workflow.step_instance (due_at) WHERE status = 'PENDING' AND overdue_notified_at IS NULL;
CREATE INDEX ix_si_instance ON workflow.step_instance (instance_id, round, step_order);

-- ===== Action =====
CREATE TABLE workflow.approval_action (
    id                uuid PRIMARY KEY,
    step_instance_id  uuid        NOT NULL REFERENCES workflow.step_instance(id),
    user_id           uuid        NOT NULL,         -- người bấm
    on_behalf_of      uuid,                          -- assignee gốc khi ủy quyền
    action            text        NOT NULL,
    comment           text,
    at                timestamptz NOT NULL,
    is_decisive       boolean     NOT NULL DEFAULT false,   -- phiếu làm bước hoàn tất
    CONSTRAINT ck_aa_action CHECK (action IN ('APPROVE','REJECT','REQUEST_REVISION','CARRIED_OVER')),
    CONSTRAINT ck_aa_comment CHECK (action NOT IN ('REJECT','REQUEST_REVISION') OR length(btrim(comment)) > 0)  -- APR-03
);
-- Mỗi user tối đa 1 phiếu / bước (group ALL và chống bấm 2 lần)
CREATE UNIQUE INDEX uq_aa_step_user ON workflow.approval_action (step_instance_id, COALESCE(on_behalf_of, user_id));
-- Phiếu "kết thúc bước" (reject/revision) tối đa 1 / bước
CREATE UNIQUE INDEX uq_aa_step_terminal ON workflow.approval_action (step_instance_id)
    WHERE action IN ('REJECT','REQUEST_REVISION');
-- Tuyến phòng thủ DB thứ hai chống duyệt trùng: mỗi bước đúng 1 phiếu quyết định (NFR-02)
CREATE UNIQUE INDEX uq_aa_one_decisive ON workflow.approval_action (step_instance_id) WHERE is_decisive;
-- Tab "Theo dõi" của inbox
CREATE INDEX ix_aa_user ON workflow.approval_action (user_id, at DESC);
