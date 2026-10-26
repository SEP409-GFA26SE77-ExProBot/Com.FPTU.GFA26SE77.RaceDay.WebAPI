# Event package checkout (iRace-style) — progress log

> Tuân thủ `.cursor/rules` (modular monolith) + FE `rule_design/AGENT_CONTEXT`.  
> **Không** đụng escrow / `CreditTicketSale` path của đăng ký VĐV.

## Goal

1. Tạo giải = chọn gói (tháng × slot) + org info + PayOS → **draft event**.
2. Chi tiết giải điền dần trong organizer panel → submit duyệt → public.
3. Giữ nguyên payment registration + finance escrow.

## Status

| Phase | Scope | Status |
|-------|--------|--------|
| BE-1 | Payment: `Purpose` + nullable `RegistrationId` + `ReferenceId` + package checkout + `PackagePaymentCompletedIntegrationEvent` | ✅ done |
| BE-2 | Event: catalog, `EventPackagePurchase`, create draft on paid, update-draft profile, endpoints | ✅ done |
| BE-3 | Public GET hide non-public unless owner/admin | ✅ done |
| BE-4 | **Promo codes**: `PromoCode` domain entity, DB-backed promo, admin CRUD, payment discount snapshot | ✅ done |
| BE-5 | **Admin package catalog**: duration/capacity options in DB + CRUD; event lock on package expiry; TotalRunnersCap locked to package | ✅ done |
| FE-1 | Replace `/events/create` with package checkout UI + PayOS | ✅ done |
| FE-2 | My Events → always open organizer panel; nav Hồ sơ giải; widen status gate | ✅ done |
| FE-3 | Checkout 3-card grid; profile quy mô VĐV read-only khi có gói | ✅ done |
| EF | Migrations … + `AddAdminPackageOptions` | ✅ done |

## Non-break constraints

- Modules talk via **Contracts + integration events** only.
- Registration `PaymentCompletedIntegrationEvent` shape stays usable; package uses **new** event.
- Finance ledger handler must ignore package payments (no ticket escrow credit).
- Existing `POST /payment/checkout` (registration) behavior unchanged.
- **Refund MUST use `PaymentOrder.Amount` (charged), never `ListAmount`** — `PaymentFinancePort` already does this.

## Completed changes

### BE-1 — Payment module

1. **Domain `PaymentOrder`**:
   - Added `PaymentPurpose` enum (`Registration`, `EventPackage`)
   - Made `RegistrationId` nullable (`Guid?`)
   - Added `ReferenceId Guid?` for package purchase reference
   - Added `CreateForPackage(referenceId, userId, amount, currency, orderCode)` factory
   - Kept `Create(...)` for registration unchanged

2. **`PaymentDto`**: added `Purpose`, `ReferenceId`; `RegistrationId` now `Guid?`

3. **Contracts**: added `PackagePaymentCompletedIntegrationEvent`
   - Fields: PaymentId, PackagePurchaseId, UserId, Amount, Currency, Gateway, TransactionReference
   - Original `PaymentCompletedIntegrationEvent` untouched

4. **`ConfirmPaymentPaidCommandHandler`**: branches publish based on `Purpose`:
   - `Registration` + `RegistrationId.HasValue` → `PaymentCompletedIntegrationEvent`
   - `EventPackage` + `ReferenceId.HasValue` → `PackagePaymentCompletedIntegrationEvent`

5. **`IPaymentRepository`**: added `GetPendingByReferenceAsync(purpose, referenceId)`

6. **`CreatePackageCheckoutCommand`**: mirrors `CreateCheckoutCommand` but uses `IPackageCheckoutInfoQuery`

7. **EF `PaymentDbContext`**: added `Purpose`, `ReferenceId` columns; `RegistrationId` now nullable

8. **`PaymentModule`**: `MapPost "/checkout/package"`

9. **`PaymentFinancePort`**: handles nullable `RegistrationId` (uses `?? Guid.Empty` for legacy compat)

### BE-2 — Event module

1. **`EventPackageCatalog`** (Application):
   - Durations: 1,2,3,5,6,8,10,12 months with prices
   - Capacities: 20,50,100,200,300,500,800,1000,2000 with add-on fees
   - Static dict `PromoCodes` **removed** — promo now DB-backed (see BE-4)
   - `GetCatalog()` removed → replaced by `GetPackageCatalogQuery` (async, DB-backed)

2. **Domain `EventPackagePurchase`** aggregate:
   - Status: `PendingPayment` | `Paid` | `Cancelled` | `Expired`
   - Fields: OrganizerUserId, OrgName, OrgIntro, OrgAddress, InvoiceInfo, DurationMonths, ParticipantCap, PromoCode, fees, TotalAmount, Currency, PaymentId, CreatedEventId, PackageExpiresAtUtc

3. **`EventDbContext`**: table `event.package_purchases`

4. **Contracts `IPackageCheckoutInfoQuery`**: cross-module port for Payment to query purchase

5. **Endpoints**:
   - `GET /api/v1/event/packages/catalog` (public)
   - `POST /api/v1/event/organizer/packages/purchases` (auth) → create PendingPayment purchase
   - `GET /api/v1/event/organizer/packages/purchases/{id}`
   - `PUT /api/v1/event/organizer/{eventId}/profile` → update draft/rejected event

6. **`PackagePaymentCompletedEventHandler`**: on `PackagePaymentCompletedIntegrationEvent` → mark purchase Paid, create draft `RaceEvent` shell, **call `promo.RecordRedemption()`** if purchase has PromoCode

7. **`RaceEvent`**: added `PackagePurchaseId?`, `PackageExpiresAtUtc?`, `SetPackageInfo()`, `UpdateDraftProfile()`

8. **`UpdateDraftRaceEventCommand`**: partial update for draft/rejected events

9. **`GetRaceEventByIdQuery`**: returns 404 if status not public unless caller is owner or admin

### BE-4 — Promo code management + payment discount snapshot

#### Event module (promo codes)

1. **Domain `PromoCode`** entity (`Event.Domain/Packages/PromoCode.cs`):
   - Properties: `Code` (unique, uppercase), `PercentOff` (0-1 decimal), `Status` (Active/Inactive), `ExpiresAtUtc?`, `MaxRedemptions?`, `UsedCount`, timestamps
   - Methods: `Create()`, `Activate()`, `Deactivate()`, `EnsureRedeemable(now)`, `RecordRedemption()`, `UpdateExpiry()`, `UpdateMaxRedemptions()`

2. **`IPromoCodeRepository`** (`Application/Abstractions/IRepositories.cs`):
   - `GetByCodeAsync`, `GetByIdAsync`, `ListAllAsync`, `ListActiveAsync(now)`, `ExistsByCodeAsync`, `AddAsync`, `Update`

3. **`PromoCodeRepository`** (`Infrastructure/Packages/PromoCodeRepository.cs`)

4. **`PackagePricingService`** (`Application/Packages/PackagePricingService.cs`):
   - `QuoteAsync(months, cap, promoCode?, ct)` — loads promo from DB, validates Active/not-expired/under-max-uses
   - Replaces old static `EventPackageCatalog.Quote()` in `CreatePackagePurchaseCommandHandler`

5. **`EventPackageCatalog`** updated:
   - Removed static `PromoCodes` dict and `Quote()` method
   - `CatalogResponse.PromoCodes` now `IReadOnlyList<PromoCodeInfo>` with `{ Code, PercentOff, ExpiresAtUtc? }`
   - Catalog served via `GetPackageCatalogQuery` (async MediatR handler)

6. **`PackagePaymentCompletedEventHandler`**: after `MarkPaid`, calls `promo.RecordRedemption()` + saves

7. **Seed** in `EventDataSeeder.SeedPromoCodesAsync`:
   - `RACEDAY10` (10%), `RACEDAY20` (20%), Active, expires ~2 years, MaxRedemptions null

8. **Admin endpoints** (`/api/v1/event/admin/promos`, AdminOnly):
   - `GET /` → list all promo codes
   - `POST /` → create `{ code, percentOff, expiresAtUtc?, maxRedemptions? }`
   - `PATCH /{promoId}` → activate/deactivate, update expiry/max

9. **EF migration `AddPromoCodes`**: table `event.promo_codes` with unique index on `Code`

#### Payment module (discount snapshot)

1. **Domain `PaymentOrder`** — new properties:
   - `ListAmount` (decimal? — giá trước giảm; for registration = Amount)
   - `DiscountAmount` (decimal? default 0)
   - `PromoCode` (string?)

2. **`CreateForPackage()`** updated: accepts `listAmount, discountAmount, promoCode` → `Amount = listAmount - discountAmount`

3. **`Create()` (registration)**: sets `ListAmount=Amount`, `DiscountAmount=0`, `PromoCode=null`

4. **`PackageCheckoutInfo`** (Event.Contracts): now includes `ListAmount, DiscountAmount, PromoCode, TotalAmount`

5. **`PaymentDto`**: exposes `listAmount, discountAmount, promoCode, amount` (charged)

6. **EF migration `AddPaymentDiscountSnapshot`**: adds 3 nullable columns to `payment.payments`

7. **Refund safety**: `PaymentFinancePort.Map()` uses `p.Amount` (charged amount), never `ListAmount`

### DI registrations

- Event Infrastructure: `IEventPackagePurchaseRepository`, `IPromoCodeRepository`, `PackagePricingService`, `IPackageCheckoutInfoQuery`
- Payment Application already references `Event.Contracts`

## Migrations

```bash
# Event module — promo_codes table
dotnet ef migrations add AddPromoCodes \
  --project src/Modules/Event/RaceDay.Modules.Event.Infrastructure \
  --startup-project src/Hosts/RaceDay.Api \
  --context EventDbContext

# Payment module — ListAmount, DiscountAmount, PromoCode columns
dotnet ef migrations add AddPaymentDiscountSnapshot \
  --project src/Modules/Payment/RaceDay.Modules.Payment.Infrastructure \
  --startup-project src/Hosts/RaceDay.Api \
  --context PaymentDbContext
```

## API contracts for FE

### Catalog (updated)

```
GET /api/v1/event/packages/catalog
→ {
    durations: [{ months, price }],
    capacities: [{ maxParticipants, addOnFee }],
    promoCodes: [{ code, percentOff, expiresAtUtc? }]  // ← objects, not strings
  }
```

### Admin promo CRUD

```
GET    /api/v1/event/admin/promos         → PromoCodeDto[]
POST   /api/v1/event/admin/promos         ← { code, percentOff, expiresAtUtc?, maxRedemptions? }
PATCH  /api/v1/event/admin/promos/{id}    ← { active?, expiresAtUtc?, clearExpiry?, maxRedemptions?, clearMaxRedemptions? }
```

`PromoCodeDto`: `{ id, code, percentOff, status, expiresAtUtc?, maxRedemptions?, usedCount, createdAtUtc, updatedAtUtc }`

### PaymentDto (updated)

```json
{
  "id": "...",
  "purpose": "registration" | "event_package",
  "amount": 900000,
  "listAmount": 1000000,
  "discountAmount": 100000,
  "promoCode": "RACEDAY10",
  "currency": "VND",
  ...
}
```

- `amount` = charged = listAmount - discountAmount
- Registration payments: `listAmount = amount`, `discountAmount = 0`, `promoCode = null`

### PackageCheckoutInfo (cross-module, updated)

```
{ purchaseId, userId, listAmount, discountAmount, promoCode?, totalAmount, currency, status, description }
```

## Notes / decisions

- Catalog pricing: durations/capacities remain static in code; promo codes are DB-backed with admin CRUD.
- Draft shell: name = org name, location = "Chưa cập nhật", cap = selected capacity.
- Promo validation: Active status + not expired + under max redemptions. `RecordRedemption()` called on payment completion.
- Package expiry = now + durationMonths + 7 days prep buffer.
- Finance `PaymentCompletedLedgerHandler` is safe: only listens to `PaymentCompletedIntegrationEvent` (registration). Package uses separate `PackagePaymentCompletedIntegrationEvent`.
- Draft from package includes **1 placeholder distance (5K)** so `SubmitForApproval` distance invariant passes; organizer chỉnh sau.
- **Refund MUST use `PaymentOrder.Amount` (charged), never `ListAmount`.**
- **Deploy:** `dotnet ef database update` cho cả `PaymentDbContext` và `EventDbContext`.

## FE summary

- `/events/create` → `EventPackageCheckout` (org + gói + PayOS)
- `/events/create/package-return` → sync → `/organizer/events/{id}/profile`
- My Events: mọi status vào organizer panel
- Nav **Hồ sơ giải**; gate status gồm draft/pending/rejected
- Catalog `promoCodes` is now `PromoCodeInfo[]` objects (not `string[]`)
- PaymentDto now has `listAmount`, `discountAmount`, `promoCode` fields
- Checkout preview % từ catalog; `/payments` hiện gốc / mã / giảm khi có KM
- Checkout: grid **3 card/hàng**; quy mô VĐV trên hồ sơ giải **read-only** nếu có `packagePurchaseId`
- Escrow VĐV / registration checkout **không đổi**

## Admin package catalog (thời hạn + quy mô)

| | |
|--|--|
| Duration | `GET/POST/PATCH /api/v1/event/admin/package-durations` — name, months, price |
| Capacity | `GET/POST/PATCH /api/v1/event/admin/package-capacities` — name, maxParticipants, addOnFee |
| Catalog public | `GET /packages/catalog` đọc DB Active |
| Event.TotalRunnersCap | = ParticipantCap gói — **không cho sửa** |
| Expiry lock | `PackageExpiresAtUtc` hết → status `suspended` (job 5 phút + gate domain) |

## Promo codes — **Admin hệ thống** (không phải organizer)

| | |
|--|--|
| Ai tạo | **Admin RaceDay** — `POST/PATCH/GET /api/v1/event/admin/promos` |
| Persist | `event.promo_codes` (Active/Inactive, expires, maxUses, usedCount) |
| Seed | `RACEDAY10` (10%), `RACEDAY20` (20%) |
| Purchase | lưu PromoCode + Discount + TotalAmount |
| Payment snapshot | `ListAmount`, `DiscountAmount`, `PromoCode`; **`Amount` = đã charge** |
| Refund | **chỉ** theo `PaymentOrder.Amount` (đã charge), không hoàn list price |
