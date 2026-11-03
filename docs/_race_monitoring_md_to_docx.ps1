# Sync docs/RaceMonitoring_API_Flows.md -> docs/RaceMonitoring_API_Guide.docx
# Dùng khi máy không có Python. Cùng nguồn với docs/_race_monitoring_md_to_docx.py.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$mdPath = Join-Path $root "RaceMonitoring_API_Flows.md"
$outPath = Join-Path $root "RaceMonitoring_API_Guide.docx"
$md = Get-Content -Path $mdPath -Raw -Encoding UTF8
$lines = $md -split "`r?`n", -1

function XmlEscape([string]$text) {
    if ($null -eq $text) { return "" }
    return ($text -replace "&", "&amp;" -replace "<", "&lt;" -replace ">", "&gt;")
}

function InlineRuns([string]$text, [bool]$mono) {
    $tick = [char]96
    $pattern = '(\*\*[^*]+\*\*|`[^`]+`)'
    $parts = [regex]::Split($text, $pattern)
    $xml = ""
    foreach ($part in $parts) {
        if ([string]::IsNullOrEmpty($part)) { continue }
        $value = $part
        $bold = $false
        $code = $mono
        $size = $null
        if ($part.StartsWith("**") -and $part.EndsWith("**") -and $part.Length -ge 4) {
            $value = $part.Substring(2, $part.Length - 4)
            $bold = $true
        }
        elseif ($part.Length -ge 2 -and $part.StartsWith($tick) -and $part.EndsWith($tick)) {
            $value = $part.Substring(1, $part.Length - 2)
            $code = $true
            $size = "18"
        }
        $fonts = if ($code) { "Consolas" } else { "Calibri" }
        $rPr = "<w:rFonts w:ascii=`"$fonts`" w:hAnsi=`"$fonts`" w:eastAsia=`"Calibri`"/>"
        if ($bold) { $rPr += "<w:b/>" }
        if ($size) { $rPr += "<w:sz w:val=`"$size`"/><w:szCs w:val=`"$size`"/><w:highlight w:val=`"lightGray`"/>" }
        if ($mono -and -not $size) { $rPr += "<w:sz w:val=`"16`"/><w:szCs w:val=`"16`"/><w:color w:val=`"333333`"/>" }
        $xml += "<w:r><w:rPr>$rPr</w:rPr><w:t xml:space=`"preserve`">$(XmlEscape $value)</w:t></w:r>"
    }
    return $xml
}

function Paragraph([string]$text, [string]$style, [bool]$mono, [bool]$italic) {
    $pPr = ""
    if ($style) { $pPr = "<w:pPr><w:pStyle w:val=`"$style`"/></w:pPr>" }
    $runs = InlineRuns $text $mono
    if ($italic -and $runs) {
        $runs = $runs -replace "<w:rPr>", "<w:rPr><w:i/>"
    }
    return "<w:p>$pPr$runs</w:p>"
}

function TableXml([string[]]$tableLines) {
    $rows = @()
    foreach ($line in $tableLines) {
        $trim = $line.Trim()
        if (-not $trim.StartsWith("|")) { continue }
        $cells = @($trim.Trim("|").Split("|") | ForEach-Object { $_.Trim() })
        if ($cells | Where-Object { $_ -match "^:?-+:?$" } | Select-Object -First 1) {
            $allRule = $true
            foreach ($cell in $cells) {
                if ($cell -notmatch "^:?-+:?$") { $allRule = $false }
            }
            if ($allRule) { continue }
        }
        $rows += ,$cells
    }
    if ($rows.Count -eq 0) { return "" }
    $cols = ($rows | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
    $xml = "<w:tbl><w:tblPr><w:tblStyle w:val=`"TableGrid`"/><w:tblW w:w=`"5000`" w:type=`"pct`"/></w:tblPr>"
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $xml += "<w:tr>"
        for ($j = 0; $j -lt $cols; $j++) {
            $val = if ($j -lt $rows[$i].Count) { $rows[$i][$j] } else { "" }
            $runs = InlineRuns $val $false
            if ($i -eq 0) { $runs = $runs -replace "<w:rPr>", "<w:rPr><w:b/>" }
            $xml += "<w:tc><w:tcPr><w:tcW w:w=`"2000`" w:type=`"dxa`"/></w:tcPr><w:p>$runs</w:p></w:tc>"
        }
        $xml += "</w:tr>"
    }
    $xml += "</w:tbl><w:p/>"
    return $xml
}

$body = New-Object System.Text.StringBuilder
[void]$body.Append((Paragraph "RaceDay — RaceMonitoring API" "Title" $false $false))
[void]$body.Append((Paragraph "Nguồn: docs/RaceMonitoring_API_Flows.md. Base URL: http://localhost:5080. JSON camelCase. Auth: Bearer. Chủ giải hoặc Coordinator Active." $null $false $false))

$i = 0
if ($lines.Count -gt 0 -and $lines[0].StartsWith("# ")) { $i = 1 }
$inCode = $false
$codeBuf = New-Object System.Collections.Generic.List[string]
$tableBuf = New-Object System.Collections.Generic.List[string]

function Flush-Table {
    if ($tableBuf.Count -gt 0) {
        [void]$body.Append((TableXml $tableBuf.ToArray()))
        $tableBuf.Clear()
    }
}

while ($i -lt $lines.Count) {
    $line = $lines[$i]
    if ($inCode) {
        if ($line.Trim().StartsWith('```')) {
            foreach ($codeLine in $codeBuf) {
                [void]$body.Append((Paragraph $codeLine $null $true $false))
            }
            if ($codeBuf.Count -eq 0) { [void]$body.Append((Paragraph " " $null $true $false)) }
            $inCode = $false
            $codeBuf.Clear()
        }
        else { $codeBuf.Add($line) }
        $i++
        continue
    }
    if ($line.Trim().StartsWith('```')) {
        Flush-Table
        $inCode = $true
        $i++
        continue
    }
    if ($line.Trim().StartsWith("|")) {
        $tableBuf.Add($line)
        $i++
        if ($i -ge $lines.Count -or -not $lines[$i].Trim().StartsWith("|")) { Flush-Table }
        continue
    }
    Flush-Table
    if ($line.Trim() -eq "---" -or $line.Trim() -eq "") { $i++; continue }
    if ($line.StartsWith("#### ")) { [void]$body.Append((Paragraph $line.Substring(5) "Heading3" $false $false)) }
    elseif ($line.StartsWith("### ")) { [void]$body.Append((Paragraph $line.Substring(4) "Heading2" $false $false)) }
    elseif ($line.StartsWith("## ")) { [void]$body.Append((Paragraph $line.Substring(3) "Heading1" $false $false)) }
    elseif ($line.StartsWith("# ")) { [void]$body.Append((Paragraph $line.Substring(2) "Heading1" $false $false)) }
    elseif ($line.StartsWith("> ")) { [void]$body.Append((Paragraph ("Ghi chú: " + $line.Substring(2).Trim()) $null $false $true)) }
    elseif ($line.StartsWith("- ")) { [void]$body.Append((Paragraph $line.Substring(2) "ListParagraph" $false $false)) }
    else { [void]$body.Append((Paragraph $line $null $false $false)) }
    $i++
}
Flush-Table
[void]$body.Append("<w:sectPr><w:pgSz w:w=`"12240`" w:h=`"15840`"/><w:pgMar w:top=`"720`" w:right=`"720`" w:bottom=`"720`" w:left=`"720`"/></w:sectPr>")

$document = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>$($body.ToString())</w:body>
</w:document>
"@

$styles = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Calibri"/><w:sz w:val="22"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="40"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="32"/><w:color w:val="1F4E79"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="26"/><w:color w:val="2E75B6"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="360"/></w:pPr></w:style>
  <w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:left w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:bottom w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:right w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:insideV w:val="single" w:sz="4" w:space="0" w:color="auto"/></w:tblBorders></w:tblPr></w:style>
</w:styles>
"@

$contentTypes = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>
"@

$rels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
"@

$docRels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
"@

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $outPath) { Remove-Item $outPath -Force }
$zip = [System.IO.Compression.ZipFile]::Open($outPath, [System.IO.Compression.ZipArchiveMode]::Create)
function Add-Entry([string]$name, [string]$content) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $writer = New-Object System.IO.StreamWriter($stream, [System.Text.UTF8Encoding]::new($false))
    $writer.Write($content)
    $writer.Dispose()
    $stream.Dispose()
}
Add-Entry "[Content_Types].xml" $contentTypes
Add-Entry "_rels/.rels" $rels
Add-Entry "word/document.xml" $document
Add-Entry "word/_rels/document.xml.rels" $docRels
Add-Entry "word/styles.xml" $styles
$zip.Dispose()
Write-Output ("Wrote {0} ({1} bytes)" -f $outPath, (Get-Item $outPath).Length)
