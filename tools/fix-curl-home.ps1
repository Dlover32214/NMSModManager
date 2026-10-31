$ErrorActionPreference = 'Stop'

# Fixed version: never reuse $home (PowerShell built-in = C:\Users\<user>).
# Create and clean up only inside a dedicated, controlled root.
$tempRoot = Join-Path (Split-Path -Parent $PSScriptRoot) '.tmp'
[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

$curlHome = Join-Path $tempRoot ('curl-home-' + [guid]::NewGuid().ToString('N'))

$forbidden = @($env:USERPROFILE, $env:APPDATA, $env:LOCALAPPDATA) | Where-Object { $_ }
foreach ($f in $forbidden) {
    if ($curlHome.TrimEnd('\') -ieq $f.TrimEnd('\')) { throw "Refused: temp dir equals $f" }
}
if (-not $curlHome.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refused: temp dir is outside the controlled root: $curlHome"
}

[System.IO.Directory]::CreateDirectory($curlHome) | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $curlHome '.curlrc') -Value 'silent' -Encoding ASCII
    $env:CURL_HOME = $curlHome

    $code = & curl.exe -s -o NUL -w '%{http_code}' https://api.github.com
    "CURL_HOME=$curlHome"
    "HTTP=$code"
}
finally {
    Remove-Item Env:CURL_HOME -ErrorAction SilentlyContinue

    $inside = $curlHome.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)
    if ($inside -and (Test-Path -LiteralPath $curlHome)) {
        Remove-Item -LiteralPath $curlHome -Recurse -Force
    }
    "CLEANED=$(-not (Test-Path -LiteralPath $curlHome))"
}
