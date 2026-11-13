# WF-02 backend test evidence

The `WF-02 Backend Tests` GitHub Actions workflow runs when a commit is pushed to
`volunteer-api`, and for pull requests targeting `volunteer-api` or `main`.
Pushing to `main` is not required. The workflow runs tests and uploads TRX reports;
it does not deploy the application or perform real payments.

## Automated scope

| Feature | WF-02 test cases | Source |
| --- | ---: | --- |
| MF-05 BIB allocation and QR confirmation | 6 | `CheckInWorkflowTests`: approval creates a usable QR, duplicate and out-of-range BIB rejection, and three ineligible registration states |
| MF-06 QR/BIB check-in | 16 | `CheckInWorkflowTests`: organizer/staff/admin access, wrong-event and invalid QR, unauthorized/anonymous callers, five ineligible states, duplicate replay, and user-ID QR lookup |
| MF-07 checkpoint scan processing | 11 | `CheckpointScanRecorderTests` (8) and `CheckpointScanTests` (3) |
| Total | 33 | Test cases tagged `Workflow=WF-02` |

Parameterized test rows count as separate executed test cases. Each row is counted
once. State-transition assertions are included in these cases and must not be
added again to inflate the total.

The CI jobs run the complete Event and RaceMonitoring unit-test projects, including
these 33 tagged cases and their surrounding regression coverage. Download
`wf-02-Event-results` and `wf-02-RaceMonitoring-results` from the successful Actions
run to inspect the TRX results. Record the run URL and commit SHA in the defense
report; a local pass alone is not evidence of a successful GitHub Actions run.

## Run locally

```powershell
dotnet test tests/RaceDay.Modules.Event.UnitTests/RaceDay.Modules.Event.UnitTests.csproj --configuration Release --filter "Workflow=WF-02"
dotnet test tests/RaceDay.Modules.RaceMonitoring.UnitTests/RaceDay.Modules.RaceMonitoring.UnitTests.csproj --configuration Release --filter "Workflow=WF-02"
```

Remove the filter to reproduce the full CI project suites. No running database,
SMTP server, PayOS account, or physical RFID reader is required for these tests;
repository and external-service dependencies are substituted.

## Coverage limits

- These are backend unit/application-handler tests, not browser or staging E2E tests.
- The approval-to-QR-to-check-in test exercises multiple handlers with substituted
  dependencies; it does not prove HTTP middleware, real persistence, or deployment.
- Offline roster storage, queue synchronization in UserWeb, physical RFID readers,
  and physical race-kit handover are outside this backend pipeline.
- Duplicate checkpoint submissions are identified by scan ID. A later passage of
  the same checkpoint with a new scan ID is a separate valid record.
- A successful run covers these two projects, not the entire solution.

The defense table should list 33 backend automated cases only after the corresponding
CI run succeeds. Browser/staging E2E and offline test counts remain separate and
require their own execution evidence.
