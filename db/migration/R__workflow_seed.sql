-- F03-T15: định nghĩa quy trình mặc định (version 1, ACTIVE) cho 4 loại hồ sơ – mọi môi trường.
-- Chỉ chèn khi chưa có (id cố định, ON CONFLICT DO NOTHING): không bao giờ ghi đè cấu hình Admin đã sửa.
INSERT INTO workflow.workflow_definition (id, document_type, version, status, definition, resubmit_policy, activated_at)
VALUES
('0192f0c0-0000-7000-8000-000000000001', 'CONTRACT', 1, 'ACTIVE', $${
  "documentType": "CONTRACT",
  "resubmitPolicy": "RESTART",
  "steps": [
    { "order": 1, "name": "Rà soát Kinh doanh",
      "assignee": { "type": "ROLE", "value": "SALES_MANAGER", "scope": "SAME_DEPARTMENT_AS_SUBMITTER" },
      "onReject": "BACK_TO_SUBMITTER", "condition": null, "slaHours": 24, "requiresAllApprovers": false },
    { "order": 2, "name": "Pháp chế",
      "assignee": { "type": "ROLE", "value": "LEGAL", "scope": "ANY" },
      "onReject": "TERMINATE", "condition": null, "slaHours": 48, "requiresAllApprovers": false },
    { "order": 3, "name": "Ban Giám đốc",
      "assignee": { "type": "GROUP", "value": "DIRECTOR", "scope": "ANY", "userIds": [] },
      "onReject": "TERMINATE", "condition": null, "slaHours": 48, "requiresAllApprovers": false }
  ],
  "onFinalApprove": { "emitESign": true }
}$$::jsonb, 'RESTART', now()),
('0192f0c0-0000-7000-8000-000000000002', 'ADDENDUM', 1, 'ACTIVE', $${
  "documentType": "ADDENDUM",
  "resubmitPolicy": "RESTART",
  "steps": [
    { "order": 1, "name": "Rà soát Kinh doanh",
      "assignee": { "type": "ROLE", "value": "SALES_MANAGER", "scope": "SAME_DEPARTMENT_AS_SUBMITTER" },
      "onReject": "BACK_TO_SUBMITTER", "condition": null, "slaHours": 24, "requiresAllApprovers": false },
    { "order": 2, "name": "Pháp chế",
      "assignee": { "type": "ROLE", "value": "LEGAL", "scope": "ANY" },
      "onReject": "BACK_TO_SUBMITTER", "condition": "addendumType != 'EXTENSION' or amount > 500000000",
      "slaHours": 48, "requiresAllApprovers": false },
    { "order": 3, "name": "Ban Giám đốc",
      "assignee": { "type": "GROUP", "value": "DIRECTOR", "scope": "ANY", "userIds": [] },
      "onReject": "TERMINATE", "condition": null, "slaHours": 48, "requiresAllApprovers": false }
  ],
  "onFinalApprove": { "emitESign": true }
}$$::jsonb, 'RESTART', now()),
('0192f0c0-0000-7000-8000-000000000003', 'PRICE_LIST', 1, 'ACTIVE', $${
  "documentType": "PRICE_LIST",
  "resubmitPolicy": "RESUME_AT_REQUESTER",
  "steps": [
    { "order": 1, "name": "Rà soát Kinh doanh",
      "assignee": { "type": "ROLE", "value": "SALES_MANAGER", "scope": "SAME_DEPARTMENT_AS_SUBMITTER" },
      "onReject": "BACK_TO_SUBMITTER", "condition": null, "slaHours": 24, "requiresAllApprovers": false },
    { "order": 2, "name": "Ban Giám đốc",
      "assignee": { "type": "GROUP", "value": "DIRECTOR", "scope": "ANY", "userIds": [] },
      "onReject": "BACK_TO_SUBMITTER", "condition": "scope == 'GLOBAL' or maxDeltaPct > 10",
      "slaHours": 48, "requiresAllApprovers": false }
  ],
  "onFinalApprove": { "emitESign": false }
}$$::jsonb, 'RESUME_AT_REQUESTER', now()),
('0192f0c0-0000-7000-8000-000000000004', 'PAYMENT_SHEET', 1, 'ACTIVE', $${
  "documentType": "PAYMENT_SHEET",
  "resubmitPolicy": "RESTART",
  "steps": [
    { "order": 1, "name": "Kế toán trưởng",
      "assignee": { "type": "ROLE", "value": "CHIEF_ACCOUNTANT", "scope": "ANY" },
      "onReject": "BACK_TO_SUBMITTER", "condition": null, "slaHours": 24, "requiresAllApprovers": false },
    { "order": 2, "name": "Ban Giám đốc",
      "assignee": { "type": "GROUP", "value": "DIRECTOR", "scope": "ANY", "userIds": [] },
      "onReject": "BACK_TO_SUBMITTER", "condition": null, "slaHours": 48, "requiresAllApprovers": false }
  ],
  "onFinalApprove": { "emitESign": true }
}$$::jsonb, 'RESTART', now())
ON CONFLICT DO NOTHING;
