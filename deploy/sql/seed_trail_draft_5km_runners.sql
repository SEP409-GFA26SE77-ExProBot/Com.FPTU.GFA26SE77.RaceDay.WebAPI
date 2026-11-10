-- Seed 24 Confirmed runners for RaceDay Trail Draft 2027 / 5KM Draft.
-- Eligible for Simulation: Confirmed + BIB + RaceStatus NotStarted + no start/finish/result.
-- File must stay UTF-8 (Vietnamese diacritics).
-- Idempotent: replaces prior seed rows with the same Id prefix.

BEGIN;

-- Event / distance (fixed seed ids from seed_demo.sql)
-- EventId:    c0ffeeee-0001-4000-8000-000000000001  RaceDay Trail Draft 2027
-- DistanceId: c0ffeeee-0001-4000-8000-0000000000d1  5KM Draft  (BibPrefix=5, 1..500)

DELETE FROM event.event_registration_checkpoint_passages
WHERE "RegistrationId" IN (
  SELECT "Id" FROM event.event_registrations
  WHERE "Id"::text LIKE 'c0ffeeee-b001-4000-8000-%'
);

DELETE FROM event.event_registration_race_status_changes
WHERE "RegistrationId" IN (
  SELECT "Id" FROM event.event_registrations
  WHERE "Id"::text LIKE 'c0ffeeee-b001-4000-8000-%'
);

DELETE FROM event.event_registrations
WHERE "Id"::text LIKE 'c0ffeeee-b001-4000-8000-%'
   OR (
     "EventId" = 'c0ffeeee-0001-4000-8000-000000000001'
     AND "DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1'
     AND "UserId"::text LIKE 'c0ffeeee-a001-4000-8000-%'
   );

INSERT INTO event.event_registrations (
  "Id","EventId","DistanceId","UserId","Status","RaceStatus",
  "FullName","Email","Phone","DateOfBirth","Gender","IdNumber",
  "HeightCm","WeightKg","BloodType","EmergencyName","EmergencyPhone",
  "ShirtType","ShirtSize","ShirtColor","MedicalCodesJson",
  "HealthCertFileName","HealthCertUrl","LiabilityAccepted","LiabilityAcceptedAtUtc",
  "RejectionReason","BibNumber","CreatedAtUtc","UpdatedAtUtc",
  "QuotedAmount","QuotedPhaseKey","QuotedPhaseName",
  "CheckedInAtUtc","CheckedInByUserId","FinishedAtUtc","ResultSeconds","StartedAtUtc"
) VALUES
('c0ffeeee-b001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000001','Confirmed','NotStarted',
 'Nguyễn Minh Anh','minh.anh.trail@raceday.vn','0905110001',DATE '1998-03-12','Female','079198000001',162,52,'O','Nguyễn Văn B','0905111001','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50001',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000002','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000002','Confirmed','NotStarted',
 'Trần Hoàng Long','hoang.long.trail@raceday.vn','0905110002',DATE '1996-07-21','Male','079196000002',176,68,'A','Trần Thị C','0905111002','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50002',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000003','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000003','Confirmed','NotStarted',
 'Lê Thị Mai','thi.mai.trail@raceday.vn','0905110003',DATE '2000-01-08','Female','079200000003',158,48,'B','Lê Văn D','0905111003','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50003',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000004','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000004','Confirmed','NotStarted',
 'Phạm Quốc Bảo','quoc.bao.trail@raceday.vn','0905110004',DATE '1995-11-30','Male','079195000004',178,72,'O','Phạm Thị E','0905111004','T-Shirt','XL','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50004',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000005','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000005','Confirmed','NotStarted',
 'Hoàng Thùy Linh','thuy.linh.trail@raceday.vn','0905110005',DATE '1999-05-19','Female','079199000005',160,50,'AB','Hoàng Văn F','0905111005','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50005',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000006','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000006','Confirmed','NotStarted',
 'Vũ Đức Thành','duc.thanh.trail@raceday.vn','0905110006',DATE '1997-09-03','Male','079197000006',174,70,'A','Vũ Thị G','0905111006','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50006',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000007','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000007','Confirmed','NotStarted',
 'Đặng Ngọc Hà','ngoc.ha.trail@raceday.vn','0905110007',DATE '2001-02-14','Female','079201000007',165,54,'O','Đặng Văn H','0905111007','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50007',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000008','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000008','Confirmed','NotStarted',
 'Bùi Anh Tuấn','anh.tuan.trail@raceday.vn','0905110008',DATE '1994-12-25','Male','079194000008',180,75,'B','Bùi Thị I','0905111008','T-Shirt','XL','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50008',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000009','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000009','Confirmed','NotStarted',
 'Đỗ Thị Phương','thi.phuong.trail@raceday.vn','0905110009',DATE '1998-08-07','Female','079198000009',163,53,'A','Đỗ Văn K','0905111009','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50009',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000a','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000a','Confirmed','NotStarted',
 'Ngô Văn Hùng','van.hung.trail@raceday.vn','0905110010',DATE '1993-04-18','Male','079193000010',172,69,'O','Ngô Thị L','0905111010','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50010',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000b','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000b','Confirmed','NotStarted',
 'Lý Thanh Tùng','thanh.tung.trail@raceday.vn','0905110011',DATE '1999-10-11','Male','079199000011',175,67,'AB','Lý Thị M','0905111011','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50011',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000c','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000c','Confirmed','NotStarted',
 'Phan Thị Kim','thi.kim.trail@raceday.vn','0905110012',DATE '2002-06-22','Female','079202000012',157,47,'O','Phan Văn N','0905111012','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50012',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000d','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000d','Confirmed','NotStarted',
 'Trương Minh Quân','minh.quan.trail@raceday.vn','0905110013',DATE '1996-03-05','Male','079196000013',177,71,'A','Trương Thị O','0905111013','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50013',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000e','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000e','Confirmed','NotStarted',
 'Huỳnh Nhật Nam','nhat.nam.trail@raceday.vn','0905110014',DATE '1997-07-28','Male','079197000014',173,66,'B','Huỳnh Thị P','0905111014','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50014',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-00000000000f','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-00000000000f','Confirmed','NotStarted',
 'Cao Thị Hương','thi.huong.trail@raceday.vn','0905110015',DATE '2000-09-16','Female','079200000015',161,51,'O','Cao Văn Q','0905111015','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50015',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000010','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000010','Confirmed','NotStarted',
 'Mai Xuân Phát','xuan.phat.trail@raceday.vn','0905110016',DATE '1995-01-09','Male','079195000016',179,74,'A','Mai Thị R','0905111016','T-Shirt','XL','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50016',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000011','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000011','Confirmed','NotStarted',
 'Lâm Mỹ Duyên','my.duyen.trail@raceday.vn','0905110017',DATE '2001-11-02','Female','079201000017',159,49,'B','Lâm Văn S','0905111017','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50017',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000012','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000012','Confirmed','NotStarted',
 'Đinh Công Sơn','cong.son.trail@raceday.vn','0905110018',DATE '1992-08-31','Male','079192000018',181,76,'O','Đinh Thị T','0905111018','T-Shirt','XL','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50018',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000013','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000013','Confirmed','NotStarted',
 'Alex Nguyen','alex.nguyen.trail@raceday.vn','0905110019',DATE '1998-04-04','Male','079198000019',175,70,'A','Lan Nguyen','0905111019','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50019',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000014','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000014','Confirmed','NotStarted',
 'Sarah Tran','sarah.tran.trail@raceday.vn','0905110020',DATE '1999-12-15','Female','079199000020',164,53,'O','David Tran','0905111020','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50020',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000015','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000015','Confirmed','NotStarted',
 'Chris Le','chris.le.trail@raceday.vn','0905110021',DATE '1997-06-20','Male','079197000021',174,68,'B','Jenny Le','0905111021','T-Shirt','L','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50021',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000016','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000016','Confirmed','NotStarted',
 'Emily Pham','emily.pham.trail@raceday.vn','0905110022',DATE '2000-02-27','Female','079200000022',160,50,'A','Tom Pham','0905111022','T-Shirt','S','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50022',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000017','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000017','Confirmed','NotStarted',
 'Kevin Hoang','kevin.hoang.trail@raceday.vn','0905110023',DATE '1996-10-08','Male','079196000023',178,73,'O','Anna Hoang','0905111023','T-Shirt','XL','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50023',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL),
('c0ffeeee-b001-4000-8000-000000000018','c0ffeeee-0001-4000-8000-000000000001','c0ffeeee-0001-4000-8000-0000000000d1','c0ffeeee-a001-4000-8000-000000000018','Confirmed','NotStarted',
 'Linh Vo','linh.vo.trail@raceday.vn','0905110024',DATE '2001-05-13','Female','079201000024',162,52,'AB','Huy Vo','0905111024','T-Shirt','M','Cam','[]'::jsonb,NULL,NULL,true,TIMESTAMPTZ '2026-11-10 03:00:00+00',NULL,'50024',TIMESTAMPTZ '2026-11-10 03:00:00+00',TIMESTAMPTZ '2026-11-10 03:10:00+00',350000.0,'phase-seed','Seed',NULL,NULL,NULL,NULL,NULL);

UPDATE event.event_distances
SET "RegisteredCount" = (
  SELECT count(*)
  FROM event.event_registrations r
  WHERE r."EventId" = 'c0ffeeee-0001-4000-8000-000000000001'
    AND r."DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1'
    AND r."Status" IN ('PendingPayment', 'PendingApproval', 'Confirmed', 'Finished')
)
WHERE "Id" = 'c0ffeeee-0001-4000-8000-0000000000d1';

COMMIT;

-- Verify simulation-eligible pool
SELECT count(*) AS eligible_count
FROM event.event_registrations
WHERE "EventId" = 'c0ffeeee-0001-4000-8000-000000000001'
  AND "DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1'
  AND "Status" = 'Confirmed'
  AND "RaceStatus" = 'NotStarted'
  AND "StartedAtUtc" IS NULL
  AND "FinishedAtUtc" IS NULL
  AND "ResultSeconds" IS NULL
  AND coalesce(btrim("BibNumber"), '') <> '';

SELECT "BibNumber", "FullName", "Gender", "Status", "RaceStatus"
FROM event.event_registrations
WHERE "EventId" = 'c0ffeeee-0001-4000-8000-000000000001'
  AND "DistanceId" = 'c0ffeeee-0001-4000-8000-0000000000d1'
ORDER BY "BibNumber";
