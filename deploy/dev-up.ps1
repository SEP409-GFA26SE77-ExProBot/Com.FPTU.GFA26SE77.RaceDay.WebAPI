# RaceDay local all-in-one:
#   Docker Desktop → compose (Postgres/Mongo/Redis/Rabbit + API profile)
#   → ngrok + PayOS webhook (live) → User FE (:3000) → Admin FE (:3001)
#
# Run:
#   .\deploy\dev-up.ps1
#   .\deploy\dev-up.cmd
#
# Useful switches:
#   -InfraOnly              chỉ infra (không API container)
#   -SkipBuild              compose up không --build
#   -SkipPayOsWebhook       bỏ ngrok / confirm-webhook
#   -SkipFrontend           không mở User/Admin FE
#   -SkipAdminFrontend      chỉ User FE
#   -NoBrowser              không mở browser
#   -FePath / -FeAdminPath  override đường dẫn FE
#
# Ngrok (PayOS live webhook):
#   1) Cài: https://ngrok.com/download  (hoặc winget install ngrok.ngrok)
#   2) Lấy authtoken: https://dashboard.ngrok.com/get-started/your-authtoken
#   3) Một lần: ngrok config add-authtoken <TOKEN>
#   4) Script tự `ngrok http 5080` + đăng ký webhook PayOS
#   Dashboard tunnel: http://127.0.0.1:4040
#   Nếu PayOS__UseMock=true (hoặc thiếu ClientId/ApiKey) → bỏ qua ngrok.

[CmdletBinding()]
param(
    [switch]$SkipFrontend,
    [switch]$SkipAdminFrontend,
    [switch]$InfraOnly,
    [switch]$SkipBuild,
    [switch]$SkipPayOsWebhook,
    [switch]$NoBrowser,
    [string]$FePath = "",
    [string]$FeAdminPath = ""
)

$ErrorActionPreference = "Stop"

$DeployDir = $PSScriptRoot
$RepoRoot = Split-Path $DeployDir -Parent
$ComposeFile = Join-Path $DeployDir "docker-compose.yml"
$EnvFile = Join-Path $RepoRoot ".env"
$SepParent = Split-Path $RepoRoot -Parent

if (-not $FePath) {
    $FePath = Join-Path $SepParent "SEP_True_Frontend_User"
}
if (-not $FeAdminPath) {
    $FeAdminPath = Join-Path $SepParent "SEP_FronteEnd_Admin\Com.FPTU.GFA26SE77.RaceDay.AdminWeb"
}

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Write-Hint([string]$msg) {
    Write-Host "    $msg" -ForegroundColor DarkGray
}

function Get-DotEnvMap([string]$path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $map }
    Get-Content $path -Encoding UTF8 | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith("#")) { return }
        $i = $line.IndexOf("=")
        if ($i -lt 1) { return }
        $k = $line.Substring(0, $i).Trim()
        $v = $line.Substring($i + 1).Trim().Trim('"').Trim("'")
        $map[$k] = $v
    }
    return $map
}

function Test-HttpOk([string]$url) {
    try {
        $null = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
        return $true
    } catch {
        return $false
    }
}

function Ensure-DockerDesktop {
    Write-Step "Docker Desktop"
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        throw "Docker CLI not found. Install Docker Desktop first."
    }

    $infoOk = $false
    try {
        docker info 1>$null 2>$null
        if ($LASTEXITCODE -eq 0) { $infoOk = $true }
    } catch { $infoOk = $false }

    if (-not $infoOk) {
        Write-Host "Starting Docker Desktop..." -ForegroundColor Yellow
        $dd = @(
            "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe",
            "${env:ProgramFiles(x86)}\Docker\Docker\Docker Desktop.exe",
            "$env:LOCALAPPDATA\Docker\Docker Desktop.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1

        if (-not $dd) {
            throw "Docker Desktop.exe not found. Open Docker manually, then re-run."
        }
        Start-Process $dd | Out-Null

        $deadline = (Get-Date).AddMinutes(3)
        do {
            Start-Sleep -Seconds 3
            try {
                docker info 1>$null 2>$null
                if ($LASTEXITCODE -eq 0) { $infoOk = $true; break }
            } catch { }
            Write-Host "  waiting for Docker engine..." -ForegroundColor DarkGray
        } while ((Get-Date) -lt $deadline)

        if (-not $infoOk) {
            throw "Docker Desktop did not become ready in 3 minutes."
        }
    }

    Write-Host "Docker OK." -ForegroundColor Green
}

function Start-Compose {
    if (-not (Test-Path $ComposeFile)) {
        throw "Missing compose file: $ComposeFile"
    }
    if (-not (Test-Path $EnvFile)) {
        Write-Host "WARN: no .env at $EnvFile — API/PayOS có thể thiếu key" -ForegroundColor Yellow
    }

    Push-Location $RepoRoot
    try {
        if ($InfraOnly) {
            Write-Step "docker compose up -d (infra only)"
            if ($SkipBuild) {
                docker compose -f $ComposeFile up -d
            } else {
                docker compose -f $ComposeFile up -d --build
            }
        } else {
            Write-Step "docker compose --profile api up -d --build"
            $cargs = @("-f", $ComposeFile, "--profile", "api", "up", "-d")
            if (-not $SkipBuild) { $cargs += "--build" }
            docker compose @cargs
        }
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose failed (exit $LASTEXITCODE)."
        }
    } finally {
        Pop-Location
    }
    Write-Host "Compose OK." -ForegroundColor Green
    Write-Hint "Postgres init SQL: deploy/sql/raceday_init.sql (chỉ chạy khi volume pgdata trống)"
    Write-Hint "API start cũng chạy EventDataSeeder → refresh banner/docs/certs Quy Nhơn (R2)"
}

function Wait-Api([int]$timeoutSec = 180) {
    if ($InfraOnly) { return }
    Write-Step "Wait API http://localhost:5080"
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:5080/api/v1/payment/status" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
                Write-Host "API ready: $($r.Content)" -ForegroundColor Green
                return
            }
        } catch { }
        Start-Sleep -Seconds 3
        Write-Host "  waiting for API..." -ForegroundColor DarkGray
    }
    Write-Host "API not ready after ${timeoutSec}s. Check: docker compose -f deploy/docker-compose.yml logs -f api" -ForegroundColor Yellow
}

function Get-NgrokHttpsUrl {
    try {
        $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 3
        $https = @($tunnels.tunnels) | Where-Object { $_.public_url -like "https://*" } | Select-Object -First 1
        if ($https) { return [string]$https.public_url }
    } catch { }
    return $null
}

function Ensure-NgrokTunnel {
    $existing = Get-NgrokHttpsUrl
    if ($existing) {
        Write-Host "ngrok already up: $existing" -ForegroundColor Green
        return $existing
    }

    $ngrok = Get-Command ngrok -ErrorAction SilentlyContinue
    if (-not $ngrok) {
        Write-Host ""
        Write-Host "NGROK MISSING — cần cho PayOS webhook live:" -ForegroundColor Yellow
        Write-Hint "1) Download: https://ngrok.com/download"
        Write-Hint "2) Authtoken: https://dashboard.ngrok.com/get-started/your-authtoken"
        Write-Hint "3) ngrok config add-authtoken <TOKEN>"
        Write-Hint "4) Re-run: .\deploy\dev-up.ps1"
        Write-Hint "Hoặc: .\deploy\dev-up.ps1 -SkipPayOsWebhook  (dùng mock / sync payment)"
        throw "ngrok not in PATH."
    }

    Write-Host "Starting ngrok http 5080..." -ForegroundColor Yellow
    Write-Hint "Nếu lỗi 'authtoken' → ngrok config add-authtoken <TOKEN>"
    Start-Process ngrok -ArgumentList @("http", "5080") -WindowStyle Minimized
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Seconds 2
        $url = Get-NgrokHttpsUrl
        if ($url) {
            Write-Host "ngrok OK: $url" -ForegroundColor Green
            Write-Hint "Inspector: http://127.0.0.1:4040"
            return $url
        }
        Write-Host "  waiting for ngrok..." -ForegroundColor DarkGray
    } while ((Get-Date) -lt $deadline)

    throw "ngrok public URL not available. Open http://127.0.0.1:4040 — kiểm tra authtoken."
}

function Register-PayOsWebhook([string]$webhookUrl, [hashtable]$envMap) {
    $clientId = $envMap["PayOS__ClientId"]
    $apiKey = $envMap["PayOS__ApiKey"]
    $base = $envMap["PayOS__BaseUrl"]
    if ([string]::IsNullOrWhiteSpace($base)) {
        $base = "https://api-merchant.payos.vn"
    }
    $base = $base.TrimEnd("/")

    if ([string]::IsNullOrWhiteSpace($clientId) -or [string]::IsNullOrWhiteSpace($apiKey)) {
        throw "Missing PayOS__ClientId / PayOS__ApiKey in .env"
    }

    $uri = "$base/confirm-webhook"
    Write-Host "PayOS confirm-webhook -> $webhookUrl" -ForegroundColor Yellow
    $body = @{ webhookUrl = $webhookUrl } | ConvertTo-Json -Compress
    $headers = @{
        "x-client-id"  = $clientId
        "x-api-key"    = $apiKey
        "Content-Type" = "application/json"
    }

    try {
        $resp = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $body -TimeoutSec 60
        $code = [string]$resp.code
        if ($code -and $code -ne "00") {
            throw "PayOS code=$code desc=$($resp.desc)"
        }
        Write-Host "PayOS webhook registered OK." -ForegroundColor Green
        if ($resp.data) {
            Write-Host "  channel: $($resp.data.name) / $($resp.data.webhookUrl)" -ForegroundColor DarkGray
        }
    } catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $msg = $_.ErrorDetails.Message }
        throw "PayOS webhook register failed: $msg | url=$webhookUrl"
    }
}

function Start-PayOsLiveWebhook {
    if ($SkipPayOsWebhook -or $InfraOnly) {
        Write-Step "PayOS webhook skipped"
        Write-Hint "Live PayOS cần URL public → ngrok. Xem hướng dẫn đầu file script."
        return
    }

    $envMap = Get-DotEnvMap $EnvFile
    $useMock = ($envMap["PayOS__UseMock"] + "").ToLowerInvariant()
    $isMock = ($useMock -eq "true") -or ($useMock -eq "1") `
        -or [string]::IsNullOrWhiteSpace($envMap["PayOS__ClientId"]) `
        -or [string]::IsNullOrWhiteSpace($envMap["PayOS__ApiKey"])

    if ($isMock) {
        Write-Step "PayOS mock mode - skip ngrok/webhook"
        Write-Hint "Bật live: set PayOS__UseMock=false + ClientId/ApiKey/ChecksumKey trong .env"
        return
    }

    Write-Step "PayOS live: ngrok + confirm-webhook"
    $public = Ensure-NgrokTunnel
    $webhookUrl = "$public/api/v1/payment/webhooks/payos"
    try { Set-Clipboard -Value $webhookUrl } catch { }
    Register-PayOsWebhook -webhookUrl $webhookUrl -envMap $envMap
    Write-Host "Webhook: $webhookUrl" -ForegroundColor Green
    Write-Hint "(đã copy clipboard nếu được)"
}

function Start-NextDev([string]$path, [string]$title, [string]$url, [int]$port) {
    if (-not (Test-Path (Join-Path $path "package.json"))) {
        throw "FE not found at: $path"
    }

    if (-not (Test-Path (Join-Path $path "node_modules"))) {
        Write-Host "npm install ($title)..." -ForegroundColor Yellow
        Push-Location $path
        try { npm install } finally { Pop-Location }
    }

    if (Test-HttpOk $url) {
        Write-Host "$title already running $url" -ForegroundColor Green
        return
    }

    Write-Host "Starting $title on :$port ..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command",
        "Set-Location `"$path`"; Write-Host '$title' -ForegroundColor Cyan; npm run dev"
    ) | Out-Null

    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpOk $url) {
            Write-Host "$title ready $url" -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 2
    }
    Write-Host "$title chưa ready trong 120s — mở cửa sổ '$title' xem lỗi (port bận?)." -ForegroundColor Yellow
}

function Start-Frontends {
    if ($SkipFrontend) { return }

    Write-Step "Frontend User (SEP_True_Frontend_User) :3000"
    Start-NextDev -path $FePath -title "RaceDay User FE :3000" -url "http://localhost:3000" -port 3000

    if (-not $SkipAdminFrontend) {
        Write-Step "Frontend Admin (SEP_FronteEnd_Admin) :3001"
        if (-not (Test-Path (Join-Path $FeAdminPath "package.json"))) {
            Write-Host "WARN: Admin FE not found at $FeAdminPath — skip" -ForegroundColor Yellow
            Write-Hint "Dùng -FeAdminPath hoặc clone đúng thư mục SEP_FronteEnd_Admin"
        } else {
            Start-NextDev -path $FeAdminPath -title "RaceDay Admin FE :3001" -url "http://localhost:3001" -port 3001
        }
    }

    if (-not $NoBrowser) {
        Start-Process "http://localhost:3000"
        if (-not $SkipAdminFrontend) { Start-Process "http://localhost:3001" }
        Start-Process "http://localhost:5080/swagger"
    }
}

try {
    Write-Host "RaceDay dev-up (Docker + API + PayOS/ngrok + User FE + Admin FE)" -ForegroundColor White
    Write-Host "Repo:      $RepoRoot"
    Write-Host "User FE:   $FePath"
    Write-Host "Admin FE:  $FeAdminPath"

    Ensure-DockerDesktop
    Start-Compose
    Wait-Api
    Start-PayOsLiveWebhook
    Start-Frontends

    Write-Step "Done"
    Write-Host "  API:       http://localhost:5080"
    Write-Host "  Swagger:   http://localhost:5080/swagger"
    Write-Host "  User FE:   http://localhost:3000"
    Write-Host "  Admin FE:  http://localhost:3001"
    Write-Host "  ngrok UI:  http://127.0.0.1:4040"
    Write-Host "  Flagship:  VnExpress Marathon Quy Nhon 2026 (id …aaa1)"
    Write-Host ""
    Write-Host "  Stop API/infra: docker compose -f deploy/docker-compose.yml --profile api down"
    Write-Host "  Skip webhook:   .\deploy\dev-up.ps1 -SkipPayOsWebhook"
    Write-Host "  Skip Admin FE:  .\deploy\dev-up.ps1 -SkipAdminFrontend"
    Write-Host "  Re-seed SQL (volume mới hoặc chạy tay upsert block trong raceday_init.sql)"
    exit 0
} catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    }
    exit 1
}
