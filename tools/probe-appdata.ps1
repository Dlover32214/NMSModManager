$ErrorActionPreference = 'Stop'

$appData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)
$userProfile = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)

"APPDATA=$appData"
"USERPROFILE=$userProfile"

if ([string]::IsNullOrWhiteSpace($appData)) {
    "RESULT=FAIL"
    "ERROR=APPDATA is empty"
    exit 1
}
if ($appData.TrimEnd('\') -ieq $userProfile.TrimEnd('\')) {
    "RESULT=FAIL"
    "ERROR=APPDATA resolved to the user profile"
    exit 1
}

$dir = Join-Path $appData ('AllModsSkyWriteProbe-' + [guid]::NewGuid().ToString('N'))
$file = Join-Path $dir 'probe.txt'

try {
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    Set-Content -LiteralPath $file -Value ("probe " + (Get-Date -Format 'o')) -Encoding UTF8
    $read = Get-Content -LiteralPath $file -Raw
    "DIR=$dir"
    "READ_LEN=$($read.Length)"
    "RESULT=OK"
}
catch {
    "RESULT=FAIL"
    "ERROR_TYPE=$($_.Exception.GetType().FullName)"
    "ERROR=$($_.Exception.Message)"
}
finally {
    if (Test-Path -LiteralPath $dir) {
        try {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
            "CLEANUP=OK"
        }
        catch {
            "CLEANUP=FAIL:$($_.Exception.Message)"
        }
    }
}
