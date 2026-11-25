param([ValidateSet('Notepad','Edge')][string]$AppName)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Windows.Forms
Add-Type 'using System;using System.Runtime.InteropServices;public class WinCheck{[DllImport("user32")]public static extern uint GetClipboardSequenceNumber();[DllImport("user32")]public static extern IntPtr GetClipboardOwner();[DllImport("user32")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);[DllImport("user32")]static extern void keybd_event(byte k,byte scan,uint fl,UIntPtr tag);public static void Press(byte mod,byte key){if(mod!=0)keybd_event(mod,0,0,UIntPtr.Zero);keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);if(mod!=0)keybd_event(mod,0,2,UIntPtr.Zero);}[DllImport("user32")]static extern void mouse_event(uint flags,int x,int y,uint data,UIntPtr extra);
public static void Translate(int mods,int key){

    byte[] modifiers={18,17,16};
    for(int i=0;i<3;i++)if((mods&(1<<i))!=0)keybd_event(modifiers[i],0,0,UIntPtr.Zero);
    if(key==5||key==6){mouse_event(0x80,0,0,(uint)(key-4),UIntPtr.Zero);mouse_event(0x100,0,0,(uint)(key-4),UIntPtr.Zero);}
    else Press(0,(byte)key);
    for(int i=2;i>=0;i--)if((mods&(1<<i))!=0)keybd_event(modifiers[i],0,2,UIntPtr.Zero);
}
public static IntPtr Card(string title){return FindWindow(null,title);}[DllImport("user32")]static extern bool AttachThreadInput(uint a,uint b,bool join);[DllImport("kernel32")]static extern uint GetCurrentThreadId();[DllImport("user32")]static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr p);public static bool FocusFixture(IntPtr w){uint a=GetCurrentThreadId(),b=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);bool joined=b!=0 && a!=b && AttachThreadInput(a,b,true);try{return SetForegroundWindow(w);}finally{if(joined)AttachThreadInput(a,b,false);}}[DllImport("user32")]public static extern IntPtr GetForegroundWindow();[DllImport("user32")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport("user32",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,string t);[DllImport("user32")]public static extern bool IsWindowVisible(IntPtr h);[DllImport("user32")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr a,IntPtr b);}'
$id = 'SelectionTest' + [guid]::NewGuid().ToString('N').Substring(0,12)
$dir = New-Item -ItemType Directory -Path (Join-Path $env:TEMP $id)
$app = Start-Process (Join-Path $PSScriptRoot 'Glossa.exe') -WindowStyle Hidden -PassThru
$prev = [WinCheck]::GetForegroundWindow()
$window = [IntPtr]::Zero
try {
    Start-Sleep -Seconds 1
    if ($app.HasExited) {throw '已有实例或启动失败'}
    if ($AppName -eq 'Edge') {
        $file = Join-Path $dir "$id.html"
        "<meta charset=utf-8><title>$id</title><p>图书馆明天上午开放。</p>" | Set-Content -LiteralPath $file -Encoding utf8
        $edge = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
        Start-Process $edge -ArgumentList @("--user-data-dir=$dir/profile",'--no-first-run',"--app=$(([uri]$file).AbsoluteUri)")
        $processName = 'msedge'
    } else {
        $file = Join-Path $dir "$id.txt"
        'The library will open tomorrow morning.' | Set-Content -LiteralPath $file -Encoding utf8
        Start-Process notepad.exe -ArgumentList $file
        $processName = 'notepad'
    }
    for ($i=0;$i -lt 48;$i++) {
        $p = Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -like "*$id*" | Select-Object -First 1
        if ($p) {$window=$p.MainWindowHandle;break}
        Start-Sleep -Milliseconds 250
    }
    if ($window -eq [IntPtr]::Zero) {throw '未找到自编材料的测试窗口'}
    $ws=New-Object -ComObject WScript.Shell
    $null=$ws.AppActivate($p.Id)
    $null=[WinCheck]::FocusFixture($window)
    try { [System.Windows.Automation.AutomationElement]::FromHandle($window).SetFocus() } catch {}
    Start-Sleep -Milliseconds 400
    $binding = '0,5'
    $config = Join-Path $env:LOCALAPPDATA '划词翻译/hotkey.txt'
    if(Test-Path $config){$binding=(Get-Content -Raw $config).Trim()}
    $mod,$key = [int[]]($binding.Split(','))
    foreach($keys in @('^a','translate')){
        Start-Sleep -Milliseconds 250
        if([WinCheck]::GetForegroundWindow() -ne $window){throw "焦点已切换，停止发送按键 foreground=$([WinCheck]::GetForegroundWindow()) target=$window"}
        if($keys -eq '^a'){[WinCheck]::Press(17,65)}else{[WinCheck]::Translate($mod,$key)}
    }
    for ($i=0;$i -lt 120;$i++) {
        Start-Sleep -Milliseconds 250
        $h=[WinCheck]::Card('Glossa')
        if ($h -eq [IntPtr]::Zero -or ![WinCheck]::IsWindowVisible($h)) {continue}
        $el=[System.Windows.Automation.AutomationElement]::FromHandle($h)
        $names=@($el.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name})
        if (($names -join ' ') -match '图书馆|library') {break}
        if (($names -join ' ') -match '取词失败|未获取|翻译失败') {throw ($names -join ' ')}
    }
    $expected=if($AppName -eq 'Edge'){'library'}else{'图书馆'}
    if($names -notcontains '复制' -or ($names -join ' ') -notmatch $expected){throw "译文核对失败 $names"}
    if([WinCheck]::GetForegroundWindow() -ne $window){throw '卡片抢焦点'}
    # Only exercise Copy with an empty or plain-text clipboard.
    $seq=[WinCheck]::GetClipboardSequenceNumber()
    $data=[System.Windows.Forms.Clipboard]::GetDataObject()
    $formats=if($data){@($data.GetFormats($false))}
    $copyStatus='复制跳过（剪贴板含其他格式）'
    if(-not ($formats | Where-Object {$_ -and $_ -notin @('Text','UnicodeText','OEMText','Locale')}) -and [WinCheck]::GetClipboardSequenceNumber() -eq $seq){
        $old=[System.Windows.Forms.Clipboard]::GetText()
        $buttonCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'复制')
        $button=$el.FindAll([System.Windows.Automation.TreeScope]::Descendants,$buttonCondition) | Where-Object {$_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button'} | Select-Object -First 1
        if(!$button){throw '找不到复制按钮'}
        if([WinCheck]::GetClipboardSequenceNumber() -ne $seq){throw '剪贴板已变化，停止复制测试'}
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 200
        $actual=[System.Windows.Forms.Clipboard]::GetText()
        $ownerId=[uint32]0
        $null=[WinCheck]::GetWindowThreadProcessId([WinCheck]::GetClipboardOwner(),[ref]$ownerId)
        if($actual -notmatch $expected -or !$names.Contains($actual)){throw '复制内容与完整译文不一致'}
        if($ownerId -eq $app.Id -and [System.Windows.Forms.Clipboard]::GetText() -ceq $actual){
            if($old){[System.Windows.Forms.Clipboard]::SetText($old)}else{[System.Windows.Forms.Clipboard]::Clear()}
        }
        if(![WinCheck]::IsWindowVisible($h)){throw '复制后卡片被关闭'}
        $copyStatus='复制完整译文'
    }
    [WinCheck]::Press(0,27)
    Start-Sleep -Milliseconds 400
    if([WinCheck]::IsWindowVisible($h)){throw 'Esc 未关闭卡片'}
    "$AppName PASS | $names | 保持焦点 | $copyStatus | Esc 关闭"
} finally {
    if($window -ne [IntPtr]::Zero){$null=[WinCheck]::PostMessage($window,16,0,0)}
    if(!$app.HasExited){$app.Kill()}
    if($prev -ne [IntPtr]::Zero){$null=[WinCheck]::SetForegroundWindow($prev)}
}





