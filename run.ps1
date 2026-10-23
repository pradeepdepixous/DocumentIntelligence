param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$dotnetExe = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $dotnetExe) {
    if ($env:PATH -notlike "*$env:LOCALAPPDATA\Microsoft\dotnet*") {
        $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    }
    & $dotnetExe run -- @ScriptArgs
} else {
    dotnet run -- @ScriptArgs
}
