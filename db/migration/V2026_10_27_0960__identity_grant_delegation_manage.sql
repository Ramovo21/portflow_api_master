-- FR-W04: mọi trưởng bộ phận là người duyệt đều tự ủy quyền được khi vắng mặt (không chỉ Ban Giám đốc).
UPDATE identity.role SET permissions = array_append(permissions, 'Delegation.Manage')
WHERE code IN ('SALES_MANAGER', 'OPERATIONS_LEAD', 'CHIEF_ACCOUNTANT', 'LEGAL')
  AND NOT ('Delegation.Manage' = ANY (permissions));
