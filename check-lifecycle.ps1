$ErrorActionPreference = 'Stop'
function Listener {
    Get-NetTCPConnection -LocalPort 11434 -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
}
function Version { (Invoke-RestMethod http://127.0.0.1:11434/api/version -TimeoutSec 2).version }
$bin = Join-Path $env:LOCALAPPDATA 'Programs/Ollama'
$saved = Get-Process -Id (Listener)
$record = Get-CimInstance Win32_Process -Filter "ProcessId=$($saved.Id)"
$manager = Get-Process -Id $record.ParentProcessId
if ($saved.Path -ne (Join-Path $bin 'ollama.exe') -or $manager.Path -ne (Join-Path $bin 'ollama app.exe') -or $record.CommandLine -notmatch '\sserve\s*$') {throw 'Unexpected initial service'}
$models = (Invoke-RestMethod http://127.0.0.1:11434/api/ps -DisableKeepAlive).models
if ($models -or (Get-NetTCPConnection -LocalPort 11434 -State Established -ErrorAction SilentlyContinue)) {throw 'Service is busy'}
function Test-Component([bool]$cold) {
    Add-Type -AssemblyName System.Net.Http,System.Web.Extensions
    $type = ([Reflection.Assembly]::LoadFrom((Resolve-Path './Glossa.exe'))).GetType('LocalTranslator')
    $o = [Activator]::CreateInstance($type,$true)
    $cb = [Delegate]::CreateDelegate([Action[string]],[Console].GetMethod('WriteLine',[type[]]@([string])))
    $before = Listener
    try {
        $task = $type.GetMethod('Translate').Invoke($o,@('图书馆明天开放。','英文',$cb,[Threading.CancellationToken]::None))
        $result = $task.GetAwaiter().GetResult()
        $owned = $type.GetField('owned',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($o)
        $live = Listener
        if ($result -notmatch 'library' -or !$live) {throw 'Translation failed'}
        if ($cold -and ($before -or !$owned -or $owned.Id -ne $live)) {throw 'Wrong cold service owner'}
        if (!$cold -and ($owned -or $before -ne $live)) {throw 'Wrong warm service owner'}
        "cold=$cold before=[$before] translating=[$live] result=$result"
    } finally {$o.Dispose()}
    Start-Sleep -Milliseconds 700
    $after = Listener
    if (($cold -and $after) -or (!$cold -and ($after -ne $before -or !(Version)))) {throw 'Unexpected listener after exit'}
    "cold=$cold afterDispose=[$after] PASS"
}
try {
    $manager.Kill()
    if (!$manager.WaitForExit(5000)) {throw 'Manager still running'}
    if (!$saved.HasExited) {$saved.Kill(); $null=$saved.WaitForExit(5000)}
    if (Listener) {throw 'Port still occupied'}
    Test-Component $true
} finally {
    if ($manager.HasExited) {Start-Process (Join-Path $bin 'ollama app.exe') -WindowStyle Hidden}
    for ($i=0;$i -lt 30;$i++){try {if(Version){break}}catch{};Start-Sleep -Milliseconds 300}
    "Restored service version=$(Version)"
}
Test-Component $false


