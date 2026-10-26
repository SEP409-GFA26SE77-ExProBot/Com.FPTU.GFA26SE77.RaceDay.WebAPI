# Backend surplus audit ↔ Frontend (2026-10-13, updated)

Nguồn: Postgres schemas + EF DbContexts + FE True User / Admin API clients.  
ERD: `D:\SEP\RaceDay_BE_ERD.drawio`.

## FE → API → thuộc tính chính

| FE file | API | Fields FE gán / đọc |
|---------|-----|---------------------|
| `api/auth.ts` | `/api/v1/iam/auth/*`, `/me` | email, username, password, fullName, phone, token |
| `api/events.ts` | `GET/POST event`, registrations, uploads | distances, ticketPricing, documents, registration fields, medicalCodes, healthCert* |
| `api/organizer.ts` | create/submit/mine/uploads/regs approve | CreateRaceEvent; **không** weather/permit/merchandise |
| `api/event-packages.ts` | profile, distances, packages, open/close ĐK | ticketPricing, documents, registration window |
| `api/race-map.ts` | `/api/v1/map/.../course` | CourseJson — **SoT route** |
| `api/finance.ts` / Admin finance | `/api/v1/finance/*` | wallet, bankAccount, payouts, refunds, settings |
| `api/event-staff.ts` | `/organizer/{id}/staff` | email, role, station, phone |
| `api/check-in.ts` | `POST /api/v1/event/check-in` | eventId, bib / qrPayload → checkedIn* |
| Admin `admin-events.ts` | pending / approve / reject | status, documents, rules |

## JSON / DB — giữ vs thừa

| Item | Verdict | Trạng thái 2026-10-13 |
|------|---------|----------------------|
| `TicketPricingJson` / `RulesJson` / `RewardsJson` / `MedicalCodesJson` | **GIỮ — FE** | — |
| `map.CourseJson` | **GIỮ — SoT** | — |
| `MerchandiseJson` | **THỪA** | DTO public trả `null` (soft) |
| Owned `Weather` (`weather_*`) | **THỪA** | DTO `null`; seed `weather: null` |
| Owned `Permit` (`permit_*`) | **THỪA** | DTO `null`; SoT = `event_documents` |
| `RouteGeometryJson` | **THỪA / legacy** | Vẫn parse nếu có (osrm/leaflet); không seed checkpoint |
| `event_checkpoints` | **THỪA / legacy** | DTO distances.checkpoints = `[]`; **chưa DROP** |
| `CheckedInAtUtc` / `CheckedInByUserId` | **GIỮ — mới** | Migration `AddRegistrationCheckIn` |
| `org_*` bank vs `finance.organizer_bank_accounts` | **TRÙNG** | SoT khuyến nghị = Finance; FE dual-write ProfileBasic |
| Payment raw / Outbox / AuditLog | **GIỮ BE-only** | Admin FE audit page chưa wire |

## Check-in

| Layer | Status |
|-------|--------|
| BE `POST /api/v1/event/check-in` | Done — organizer / admin / staff Active+UserId |
| QR gen (`BuildQrJson` + FE QRCodeSVG) | Done từ trước (approve) |
| User FE `/scan-qr` | Done — camera + BIB thủ công |
| Admin FE scanner | Mock only — chưa wire |
| DROP JWT-signed QR | Chưa (vòng sau) |

## Dead FE wrappers (không bắt buộc xóa)

- User: `listEventLedgers`, `getPaymentById`, `mockPay`
- Admin: users / audit / stats / reconciliation / ops = hardcoded / `events.json`

## Migration DROP sau (chưa làm)

1. Drop cột `MerchandiseJson`, `weather_*`, `permit_*` (owned), tùy chọn `RouteGeometryJson`.  
2. Drop bảng `event.event_checkpoints`.  
3. Unify bank → chỉ Finance (cần FE bỏ dual-write).
