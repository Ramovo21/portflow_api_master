-- F01 §9.2: dữ liệu mẫu CHỈ cho dev/test (flyway location db/seed). UUID trùng với infra/keycloak/portflow-realm.json
-- để UserDirectory dùng được ngay ngày đầu, không cần chờ job đồng bộ.
INSERT INTO identity.department (id, code, name) VALUES
    ('0192f0a0-0000-7000-8000-000000000001', 'KD', 'Phòng Kinh doanh'),
    ('0192f0a0-0000-7000-8000-000000000002', 'KT', 'Phòng Khai thác'),
    ('0192f0a0-0000-7000-8000-000000000003', 'KTOAN', 'Phòng Kế toán'),
    ('0192f0a0-0000-7000-8000-000000000004', 'PC', 'Phòng Pháp chế'),
    ('0192f0a0-0000-7000-8000-000000000005', 'BGD', 'Ban Giám đốc'),
    ('0192f0a0-0000-7000-8000-000000000006', 'IT', 'Phòng Công nghệ thông tin');

INSERT INTO identity.app_user (id, username, full_name, email, department_id, active, sync_source) VALUES
    ('0192f0a1-0000-7000-8000-000000000001', 'sales01', 'Nguyễn Văn An', 'an.nv@abc.vn', '0192f0a0-0000-7000-8000-000000000001', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000002', 'salesmgr', 'Trần Thị Bình', 'binh.tt@abc.vn', '0192f0a0-0000-7000-8000-000000000001', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000003', 'ops01', 'Lê Văn Cường', 'cuong.lv@abc.vn', '0192f0a0-0000-7000-8000-000000000002', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000004', 'acc01', 'Phạm Thị Dung', 'dung.pt@abc.vn', '0192f0a0-0000-7000-8000-000000000003', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000005', 'chiefacc', 'Bùi Văn Khoa', 'khoa.bv@abc.vn', '0192f0a0-0000-7000-8000-000000000003', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000006', 'legal01', 'Hoàng Văn Em', 'em.hv@abc.vn', '0192f0a0-0000-7000-8000-000000000004', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000007', 'director', 'Võ Thị Giang', 'giang.vt@abc.vn', '0192f0a0-0000-7000-8000-000000000005', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000008', 'director2', 'Đỗ Văn Hải', 'hai.dv@abc.vn', '0192f0a0-0000-7000-8000-000000000005', true, 'SEED'),
    ('0192f0a1-0000-7000-8000-000000000009', 'admin', 'Quản Trị', 'admin@abc.vn', '0192f0a0-0000-7000-8000-000000000006', true, 'SEED');

UPDATE identity.department SET head_user_id = '0192f0a1-0000-7000-8000-000000000002' WHERE code = 'KD';
UPDATE identity.department SET head_user_id = '0192f0a1-0000-7000-8000-000000000003' WHERE code = 'KT';
UPDATE identity.department SET head_user_id = '0192f0a1-0000-7000-8000-000000000005' WHERE code = 'KTOAN';
UPDATE identity.department SET head_user_id = '0192f0a1-0000-7000-8000-000000000007' WHERE code = 'BGD';

INSERT INTO identity.user_role (user_id, role_code) VALUES
    ('0192f0a1-0000-7000-8000-000000000001', 'SALES'),
    ('0192f0a1-0000-7000-8000-000000000002', 'SALES_MANAGER'),
    ('0192f0a1-0000-7000-8000-000000000003', 'OPERATIONS'),
    ('0192f0a1-0000-7000-8000-000000000003', 'OPERATIONS_LEAD'),
    ('0192f0a1-0000-7000-8000-000000000004', 'ACCOUNTANT'),
    ('0192f0a1-0000-7000-8000-000000000005', 'CHIEF_ACCOUNTANT'),
    ('0192f0a1-0000-7000-8000-000000000006', 'LEGAL'),
    ('0192f0a1-0000-7000-8000-000000000007', 'DIRECTOR'),
    ('0192f0a1-0000-7000-8000-000000000008', 'DIRECTOR'),
    ('0192f0a1-0000-7000-8000-000000000009', 'ADMIN');
