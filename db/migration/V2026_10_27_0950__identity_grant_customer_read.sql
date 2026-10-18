-- Thiết kế UI 04 Q6: Kế toán / Kế toán trưởng cần xem khách hàng khi đối soát bảng thanh toán (phạm vi ALL).
UPDATE identity.role SET permissions = array_append(permissions, 'Customer.Read')
WHERE code IN ('ACCOUNTANT', 'CHIEF_ACCOUNTANT') AND NOT ('Customer.Read' = ANY (permissions));
