$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http,System.Web.Extensions
$asm=[Reflection.Assembly]::LoadFrom((Resolve-Path '.\Glossa.exe'))
$t=$asm.GetType('LocalTranslator')
$o=[Activator]::CreateInstance($t,$true)
$callback=[Delegate]::CreateDelegate([Action[string]],[Console].GetMethod('WriteLine',[type[]]@([string])))
try{
    foreach($case in @(@('图书馆明天上午开放。','英文','library'),@('The library will open tomorrow morning.','中文','图书馆'),@('这个 API 怎么用？','英文','API'))){
        $target=$t.GetMethod('Target').Invoke($null,@($case[0]))
        if($target -ne $case[1]){throw '目标语言错误'}
        $task=$t.GetMethod('Translate').Invoke($o,@($case[0],$target,$callback,[Threading.CancellationToken]::None))
        $result=$task.GetAwaiter().GetResult()
        if($result -notmatch $case[2]){throw "翻译核验失败 $result"}
        Write-Output "$target PASS $result"
    }
}finally{$o.Dispose()}

