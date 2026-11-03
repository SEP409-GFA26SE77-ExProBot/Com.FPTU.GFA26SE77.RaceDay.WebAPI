# SRS §3.2–3.8.2 — Context cần sửa (BE / FE / Docs)

Nguồn: `D:\SEP\doc\Report3_Software Requirement Specification.docx`  
Phạm vi đọc: **3.2 → hết 3.8.2**  
Đối chiếu: RaceDay modular monolith (`SEP_WebAPI`), FE User / Admin, OCR (`SEP_Service_Photo`) theo `.cursor/rules` + `AGENTS.md`.

> Chỉ liệt kê **gap / lệch nghiệp vụ hoặc SRS lỗi**. Mục đã khớp (register/login/forgot, event wizard cơ bản, PayOS, check-in volunteer, photo OCR/publish/search/dispute…) **không** đưa vào bảng.

## A. Lỗi tài liệu SRS (sửa Report3 trước)

| Mục SRS | Vấn đề | Hướng sửa Docs |
|--------|--------|----------------|
| **3.2.4 Profile** | Body copy nhầm thành **Logout** (trùng 3.2.5) | Viết lại Profile: xem/sửa hồ sơ, medical/profile fields theo BR-17–25 |
| **3.2.5 Logout** | Trùng nội dung với 3.2.4 hiện tại | Giữ flow logout; bỏ duplicate |
| **3.3.4 Configure Event BIB** | Trigger/description paste nhầm Admin approval | Viết đúng: Organizer cấu hình BIB range theo distance |
| **3.3.5 Distance Map** | Cùng paste Admin approval | Viết đúng: upload/cấu hình map lộ trình |
| **3.3.4 (× nhiều)** | Staff / Licenses / Content / Supplies / Approval đều đánh số **3.3.4** | Đánh số lại 3.3.6–3.3.10 |
| **3.7.6 Photo Detail & Dispute** | **Copy nguyên 3.7.5** Search by BIB | Viết: mở chi tiết, tải/share, dispute “ảnh sai”, photographer xử lý |
| **3.8.2 Configure Platform Audit** | Title = Audit; body = **moderation nội dung** | Tách: (a) Audit log read, (b) Content/report moderation — hoặc đổi title |

## B. Gap sản phẩm vs nghiệp vụ SRS

| Mục | Context | SRS kỳ vọng | Hiện trạng | Sev | Layer |
|-----|---------|-------------|------------|-----|-------|
| 3.2.2 | Login identifier | Email + password | FE/BE login bằng **username** (email chỉ lúc register) | Med | BE/FE/Docs |
| 3.2.5 | Logout token | Session/token invalid | FE clear cookie/JWT; **không revoke** server-side | Low | BE |
| 3.3.x | Notify sau approve/reject event | Thông báo Organizer khi có hỗ trợ notify | Chủ yếu Audit/integration event; **thiếu email/in-app** | Med | BE |
| 3.4.1 | Slot hold khi chờ thanh toán | Giữ chỗ trong cửa sổ PayOS (nếu support) | Chỉ check capacity lúc create; **không hold** | Med | BE |
| **3.4.3** | **BIB + QR sau payment verified** | Flow: Payment → confirm → **BIB → QR** | Payment → `pending_approval`; BIB/QR **sau Organizer approve registration** | **High** | BE/FE/Docs |
| 3.4.4 | Ticket khi eligible | QR/BIB khi đủ điều kiện | Khớp product hiện tại nếu giữ bước approve; **lệch SRS** nếu hiểu “sau payment là có vé” | Med | FE/Docs |
| **3.5.3** | Record race finish | Staff/Organizer ghi finish (UI và/hoặc API) | BE có `POST .../finish`; **FE không wire** | **High** | FE |
| 3.5.3 | Finish overwrite | Không ghi đè ngoài ý muốn; có rule sửa | Repeat → AlreadyScanned; **không API sửa finish** | Med | BE |
| **3.6.1** | Import/manage results | Organizer quản lý / CSV import | **Không** import API; Results FE phần lớn prototype | **High** | BE/FE |
| **3.6.2** | Publish results | Trạng thái unpublished vs published | Public leaderboard = mọi `Finished`; **không gate publish** | **High** | BE/FE |
| 3.6.x | Result complaint | Khiếu nại kết quả (BR-73/75) | FE dispute route **chưa BE**; `/disputes` = **photo** dispute | Med | BE/FE |
| 3.7.4 | Unpublish gallery | Publish **và** unpublish | Domain có `Unpublish`; **thiếu API/FE** | Med | BE/FE |
| 3.7.5 | Empty vs OCR đang chạy | Không nói “không có ảnh” chỉ vì OCR chưa xong | Search chỉ published; **thiếu signal “đang xử lý”** | Low | FE |
| 3.8.1 | Admin user mgmt | CRUD/role/reset theo quyền | BE đủ hơn; Admin FE chủ yếu **list + lock/unlock** | Med | FE |
| **3.8.2** | Audit vs moderation | Body SRS = duyệt nội dung báo cáo | Admin FE Audit = **read-only log**; **không moderation queue** | **High** | BE/FE/Docs |
| NFR/Check-in | QR chống giả | Ticket không đoán được | QR JSON plain; **chưa ký** | Med | BE |
| Admin extras | Stats / reconciliation | Vận hành admin | FE **demo/static** | Med | FE |

## C. Ưu tiên backlog (đề xuất)

1. **Chốt nghiệp vụ 3.4.3**: giữ *Organizer approve rồi mới BIB/QR* → sửa SRS; hoặc đổi BE sang auto-BIB sau payment verified như SRS.  
2. **Results publish gate (3.6.2)** + organizer results UI/API (3.6.1).  
3. **Wire FE race finish (3.5.3)**.  
4. **Sửa Report3 docs** (3.2.4, 3.3 numbering, 3.7.6, 3.8.2).  
5. Photo **unpublish** API/FE; Admin **user actions** + moderation nếu giữ wording 3.8.2.

## D. Đã khớp đáng kể (không cần sửa gấp)

- 3.2.1 Register, verify email, hash password, default role  
- 3.3 Create event + profile distances/BIB/map/docs/content/supplies + Admin approve event  
- 3.4 PayOS checkout + payment status tách registration  
- 3.5.1–3.5.2 Check-in QR / manual (Admin volunteer + BE)  
- 3.7.1–3.7.5 Upload → OCR → review/correct → publish → search by BIB (+ download/dispute FE gần đây)
