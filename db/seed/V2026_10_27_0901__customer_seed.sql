-- F02-T08: 3 khách hàng mẫu (dev/test) thuộc phòng Kinh doanh; 1 khách hàng tạm ngưng để thử FR-C03.
INSERT INTO customer.customer (id, code, name, tax_code, address, representative, contacts, status, status_reason,
                               owner_department_id, created_by, updated_by)
VALUES
('0192f0b0-0000-7000-8000-000000000001', 'KH900001', 'Công ty TNHH Vận tải Biển Xanh', '0101234567',
 '12 Nguyễn Tất Thành, Q.4, TP.HCM', '{"fullName": "Trần Văn A", "position": "Giám đốc"}',
 '[{"name": "Lê Thị B", "email": "b.le@bienxanh.vn", "phone": "0903000111", "role": "Kế toán", "primary": true}]',
 'ACTIVE', NULL, '0192f0a0-0000-7000-8000-000000000001',
 '0192f0a1-0000-7000-8000-000000000001', '0192f0a1-0000-7000-8000-000000000001'),
('0192f0b0-0000-7000-8000-000000000002', 'KH900002', 'Công ty CP Logistics Sao Mai', '0309876543',
 '45 Lê Lợi, Q.1, TP.HCM', '{"fullName": "Phạm Văn C", "position": "Tổng Giám đốc"}', '[]',
 'ACTIVE', NULL, '0192f0a0-0000-7000-8000-000000000001',
 '0192f0a1-0000-7000-8000-000000000001', '0192f0a1-0000-7000-8000-000000000001'),
('0192f0b0-0000-7000-8000-000000000003', 'KH900003', 'Công ty TNHH Thương mại Hải Đăng', '0201112223',
 '8 Trần Phú, Hải Phòng', '{}', '[]',
 'SUSPENDED', 'Nợ quá hạn 90 ngày, chờ đối soát', '0192f0a0-0000-7000-8000-000000000001',
 '0192f0a1-0000-7000-8000-000000000001', '0192f0a1-0000-7000-8000-000000000001');
