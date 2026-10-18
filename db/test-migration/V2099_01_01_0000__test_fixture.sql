-- Bảng chỉ dùng cho integration test của F00 (không có trong migration thật)
CREATE SCHEMA IF NOT EXISTS test_fixture;

CREATE TABLE test_fixture.widget (
    id      uuid PRIMARY KEY,
    name    text NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ux_widget_name UNIQUE (name),
    CONSTRAINT ck_widget_name_len CHECK (length(name) <= 50)
);

CREATE TABLE test_fixture.received_event (
    event_id    uuid NOT NULL,
    widget_id   uuid NOT NULL,
    consumer    text NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now()
);
