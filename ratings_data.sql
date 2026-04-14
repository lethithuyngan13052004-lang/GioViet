-- Dữ liệu Rating cho các Trip thành công (status = 2)
-- Dựa trên thông tin từ Dump20260410.sql

USE `gio_viet_db`;

INSERT INTO `ratings` (`CustomerId`, `ReqId`, `Score`, `Comment`, `CreatedAt`) VALUES
(2, 100, 5, 'Tài xế rất nhiệt tình, hàng hóa được bảo quản tốt.', '2026-03-02 10:00:00'),
(3, 101, 4, 'Giao hàng đúng hẹn, giá cả hợp lý.', '2026-03-03 12:00:00'),
(4, 102, 5, 'Dịch vụ tuyệt vời, sẽ tiếp tục ủng hộ.', '2026-03-04 09:00:00'),
(2, 103, 5, 'Bác tài vui tính, lái xe cẩn thận.', '2026-03-05 15:00:00'),
(3, 104, 4, 'Hàng đến hơi muộn một chút nhưng vẫn hài lóng.', '2026-03-06 17:00:00'),
(4, 105, 5, 'Chất lượng phục vụ tốt, hỗ trợ bốc xếp nhiệt tình.', '2026-03-07 10:30:00'),
(2, 106, 5, 'Rất nhanh và an toàn.', '2026-03-08 12:00:00'),
(3, 107, 4, 'Ok, giá tốt.', '2026-03-09 08:30:00'),
(4, 108, 5, 'Tuyệt vời, 5 sao cho tài xế.', '2026-03-10 16:00:00'),
(2, 109, 5, 'Hàng đóng gói kỹ, tài xế thân thiện.', '2026-03-11 18:30:00'),
(3, 110, 4, 'Dịch vụ ổn, giao hàng đúng điểm hẹn.', '2026-03-12 11:00:00'),
(4, 111, 5, 'Phụ phí bốc xếp hơi cao nhưng phục vụ rất chu đáo.', '2026-03-13 13:00:00'),
(2, 112, 5, 'Giao hàng chuyên nghiệp.', '2026-03-14 09:00:00'),
(4, 129, 5, 'Rất hài lòng với chuyến đi này.', '2026-04-09 10:00:00');
