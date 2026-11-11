# Leaderboard API Guide

Các API dùng chung cho **Organizer của giải** và **Admin account**. Organizer
chỉ được thao tác trên giải do chính mình sở hữu.

## Biến sử dụng

```text
{baseUrl}        = http://localhost:5262
{eventId}        = GUID của giải
{distanceId}     = GUID của cự ly
{userId}         = GUID tài khoản vận động viên, dùng khi thêm, sửa hoặc xóa
```

Header:

```http
Authorization: Bearer {organizer-or-admin-jwt}
Accept: application/json
```

## 1. Xem bảng xếp hạng

```http
GET {baseUrl}/api/v1/event/organizer/{eventId}/distances/{distanceId}/leaderboard?page=1&pageSize=50
Authorization: Bearer {organizer-or-admin-jwt}
Accept: application/json
```

Query `page` (integer, mặc định 1, tối thiểu 1) và `pageSize` (integer, mặc
định 50, từ 1 đến 100).

Response `200 OK`:

```json
{
  "items": [
    {
      "rank": 1,
      "eventName": "HCMC Night Run",
      "distanceName": "10KM",
      "bib": "0012",
      "fullName": "Nguyen Van A",
      "gender": "Male",
      "startAtUtc": "2026-11-06T02:00:00Z",
      "finishAtUtc": "2026-11-06T03:30:00Z",
      "totalTime": "01:30:00",
      "pace": "09:00"
    }
  ],
  "totalCount": 1,
  "totalPages": 1,
  "currentPage": 1,
  "pageSize": 50
}
```

## 2. Thêm kết quả

Khi thêm mới, nhập `userId`. Server tự tìm đăng ký theo `eventId`,
`distanceId` và `userId`.

```http
POST {baseUrl}/api/v1/event/organizer/{eventId}/distances/{distanceId}/leaderboard
Authorization: Bearer {organizer-or-admin-jwt}
Content-Type: application/json

{
  "userId": "00000000-0000-0000-0000-000000000003",
  "totalTime": "01:30:00",
  "startAtUtc": "2026-11-06T02:00:00Z",
  "finishedAtUtc": "2026-11-06T03:30:00Z"
}
```

| Input field | Type | Required | Description |
|---|---|---:|---|
| `userId` | GUID | Yes | Runner account in the event and distance |
| `totalTime` | string `HH:mm:ss` | Yes | From `00:00:00` to `23:59:59` |
| `startAtUtc` | ISO-8601 datetime | Yes | Start time in UTC |
| `finishedAtUtc` | ISO-8601 datetime | Yes | Finish time in UTC |

`finishedAtUtc - startAtUtc` must exactly equal `totalTime`.

Response `201 Created`:

```json
{
  "rank": 1,
  "eventName": "HCMC Night Run",
  "distanceName": "10KM",
  "bib": "0012",
  "fullName": "Nguyen Van A",
  "gender": "Male",
  "startAtUtc": "2026-11-06T02:00:00Z",
  "finishAtUtc": "2026-11-06T03:30:00Z",
  "totalTime": "01:30:00",
  "pace": "09:00"
}
```

`rank` được tính ngay trong POST sau khi lưu kết quả.

## 3. Sửa kết quả bằng UserId

Server tìm đăng ký bằng `eventId`, `distanceId` và `userId`.

```http
PUT {baseUrl}/api/v1/event/organizer/{eventId}/distances/{distanceId}/leaderboard/user/{userId}
Authorization: Bearer {organizer-or-admin-jwt}
Content-Type: application/json

{
  "totalTime": "01:30:00",
  "startAtUtc": "2026-11-06T02:00:00Z",
  "finishedAtUtc": "2026-11-06T03:25:00Z"
}
```

Request này không hợp lệ vì `03:25:00 - 02:00:00 = 01:25:00`, không khớp
`totalTime`. Ví dụ hợp lệ:

```json
{
  "totalTime": "01:25:00",
  "startAtUtc": "2026-11-06T02:00:00Z",
  "finishedAtUtc": "2026-11-06T03:25:00Z"
}
```

Response `200 OK` có đầy đủ các field:

```json
{
  "rank": 1,
  "eventName": "HCMC Night Run",
  "distanceName": "10KM",
  "bib": "0012",
  "fullName": "Nguyen Van A",
  "gender": "Male",
  "startAtUtc": "2026-11-06T02:00:00Z",
  "finishAtUtc": "2026-11-06T03:25:00Z",
  "totalTime": "01:25:00",
  "pace": "08:30"
}
```

## 4. Xóa kết quả bằng UserId

```http
DELETE {baseUrl}/api/v1/event/organizer/{eventId}/distances/{distanceId}/leaderboard/user/{userId}
Authorization: Bearer {organizer-or-admin-jwt}
```

Response `204 No Content`, không có JSON. Kết quả bị xóa khỏi bảng xếp hạng và
đăng ký trở về `confirmed`.

## 5. Xuất PDF

```http
GET {baseUrl}/api/v1/event/organizer/{eventId}/distances/{distanceId}/leaderboard/pdf
Authorization: Bearer {organizer-or-admin-jwt}
Accept: application/pdf
```

Response `200 OK`: file binary `application/pdf`, gồm các cột `Rank`, `Bib`,
`FullName`, `Gender`, `TotalTime`, `Pace`.

## Lỗi thường gặp

| Status | Ý nghĩa |
|---:|---|
| `401` | Chưa đăng nhập hoặc JWT không hợp lệ |
| `403` | Organizer không sở hữu giải |
| `400` | Kết quả âm, GUID không hợp lệ, hoặc không tìm thấy UserId trong giải/cự ly |
| `404` | Không tìm thấy giải, cự ly hoặc đăng ký |
| `204` | Xóa thành công |
