# API samples (request / response / validation)

Đi kèm [`../RaceDay_API_Flows.md`](../RaceDay_API_Flows.md).

| File | Endpoint |
|------|----------|
| `01-register.request.json` | `POST /api/v1/iam/auth/register` |
| `02-login.request.json` / `02-login.response.json` | `POST /api/v1/iam/auth/login` |
| `03-registration.request.json` | `POST /api/v1/event/{eventId}/registrations` |
| `03-registration.validation-error.json` | 400 mẫu |
| `04-checkout.request.json` / `04-checkout.response.json` | `POST /api/v1/payment/checkout` |
| `05-refund.request.json` | `POST /api/v1/finance/runner/refunds` |
| `06-payout.request.json` / `06-payout.validation-error.json` | `POST /api/v1/finance/organizer/payouts` |
| `07-map-course.request.json` | `PUT /api/v1/map/events/{eventId}/distances/{distanceId}/course` |

Create-event body đầy đủ nằm trong section B4 của `RaceDay_API_Flows.md`.
