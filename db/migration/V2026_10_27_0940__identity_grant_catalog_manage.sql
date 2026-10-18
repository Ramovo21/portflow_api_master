-- F01 §8.3 có permission ServiceCatalog.Manage nhưng chưa gán role nào → không ai quản lý được danh mục dịch vụ (F05).
-- Gán cho ADMIN (dữ liệu tham chiếu; ADMIN vẫn không Create/Submit hồ sơ và không Workflow.Act).
UPDATE identity.role SET permissions = array_append(permissions, 'ServiceCatalog.Manage')
WHERE code = 'ADMIN' AND NOT ('ServiceCatalog.Manage' = ANY (permissions));
