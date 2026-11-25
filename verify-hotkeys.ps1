param([string]$Exe='Glossa.exe',[string]$Preview)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,WindowsBase
$asm=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path)
$t=$asm.GetType('App')
$f=[Reflection.BindingFlags]'NonPublic,Instance'
$st=[Reflection.BindingFlags]'NonPublic,Static'
$app=[Activator]::CreateInstance($t)
$app.ShutdownMode='OnExplicitShutdown'
$t.GetField('appearance',$f).SetValue($app,[Activator]::CreateInstance($asm.GetType('GlassAppearance')))
$valid=$t.GetMethod('ValidBinding',$st)
foreach($m in 0..7){
 foreach($k in @(5,6)+(48..57)+(65..90)+(112..123)){
  if($valid.Invoke($null,@([uint32]$m,[uint32]$k)) -ne ($m -gt 0 -or $k -in 5,6)){throw "Allowed $m,$k"}
 }
}
$params=[Windows.Interop.HwndSourceParameters]::new('Hotkey fixture')
$params.WindowStyle=0
$sink=[Windows.Interop.HwndSource]::new($params)
$t.GetField('sink',$f).SetValue($app,$sink)
$mouse=$t.GetMethod('Mouse',$f)
$delegate=[Delegate]::CreateDelegate($t.GetField('mouseFn',$f).FieldType,$app,$mouse)
$t.GetField('mouseFn',$f).SetValue($app,$delegate)
$activate=$t.GetMethod('ActivateBinding',$f)
function SetBinding([uint32]$m,[uint32]$k){$activate.Invoke($app,@($m,$k))}
$reg=$t.GetMethod('RegisterHotKey',$st)
$unreg=$t.GetMethod('UnregisterHotKey',$st)
$allocated=$false
$dir=[IO.Path]::Combine($env:TEMP,[guid]::NewGuid().ToString())
$path=[IO.Path]::Combine($dir,'hotkey.txt')
$t.GetField('config',$f).SetValue($app,$path)
$data=[Runtime.InteropServices.Marshal]::AllocHGlobal(32)
try{
 if(!(SetBinding 7 120)){throw 'Ctrl+Alt+Shift+F9 registration'}
 $allocated=$reg.Invoke($null,@($sink.Handle,11,[uint32]7,[uint32]121))
 if(!$allocated -or (SetBinding 7 121) -or $t.GetField('vk',$f).GetValue($app) -ne 120){throw 'Conflict rollback'}
 foreach($k in 5,6){
  foreach($m in 0,3,7){
   if(!(SetBinding $m $k)){throw 'Side binding'}
   [Runtime.InteropServices.Marshal]::WriteInt32($data,8,(($k-4) -shl 16))
   $r=$mouse.Invoke($app,@(0,[IntPtr]0x20B,$data))
   if(($r -eq [IntPtr]1) -ne ($m -eq 0)){throw 'Side matching without modifiers'}
   $t.GetField('setting',$f).SetValue($app,$true)
   $null=$mouse.Invoke($app,@(0,[IntPtr]0x20C,$data))
   $t.GetField('setting',$f).SetValue($app,$false)
  }
 }
 Add-Type 'using System;using System.Runtime.InteropServices;public static class HotkeyTestInput{[DllImport("user32")]public static extern void keybd_event(byte k,byte scan,uint flags,UIntPtr extra);}'
 if(!(SetBinding 6 6)){throw 'Ctrl+Shift+side 2 binding'}
 try{
  foreach($modifier in 17,16){[HotkeyTestInput]::keybd_event($modifier,0,0,[UIntPtr]::Zero)}
  Start-Sleep -Milliseconds 80
  $down=$mouse.Invoke($app,@(0,[IntPtr]0x20B,$data))
  if($down -ne [IntPtr]1 -or $t.GetField('pressedSide',$f).GetValue($app) -ne 6){throw 'Modified side press'}
 }finally{
  foreach($modifier in 16,17){[HotkeyTestInput]::keybd_event($modifier,0,2,[UIntPtr]::Zero)}
 }
 $t.GetField('setting',$f).SetValue($app,$true)
 $up=$mouse.Invoke($app,@(0,[IntPtr]0x20C,$data))
 if($up -ne [IntPtr]1 -or $t.GetField('pressedSide',$f).GetValue($app) -ne 0){throw 'Paired release'}
 $t.GetField('setting',$f).SetValue($app,$false)
 $timer=New-Object Windows.Threading.DispatcherTimer
 $timer.Interval=[TimeSpan]::FromMilliseconds(150)
 $timer.Add_Tick({
  $timer.Stop()
  $w=$app.Windows | Where-Object Title -eq 'Glossa 设置'
  try{
   $pages=$w.Content.FindName('Pages')
   $panel=$pages.Items[0].Content
   $rows=$panel.FindName('KeyboardRows')
   $caps=$panel.FindName('SelectionCaps')
   $save=$panel.FindName('SaveButton')
   $keys=@{}
   foreach($row in $rows.Children){foreach($b in $row.Children){$keys[[int]$b.Tag]=$b}}
   if($keys.Count -ne 53 -or !$keys[6].IsChecked){throw 'Visual keyboard initial state'}
   function Tap($b){$b.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))}
   Tap $keys[-2];Tap $keys[-4];Tap $keys[53]
   if($save.IsEnabled){throw 'Bare digit'}
   $before=$save.TranslatePoint([Windows.Point]::new(0,0),$w)
   $pages.SelectedIndex=1;$w.UpdateLayout()
   $other=$pages.Items[1].Content.FindName('SaveButton')
   $after=$other.TranslatePoint([Windows.Point]::new(0,0),$w)
   if($before -ne $after -or $save.ActualWidth -ne $other.ActualWidth -or $save.ActualHeight -ne $other.ActualHeight){throw "Save placement: $before vs $after"}
   if($panel.IsVisible -or !$pages.Items[1].Content.IsVisible){throw 'Appearance navigation'}
   $pages.SelectedIndex=0;$w.UpdateLayout()
   if(!$panel.IsVisible -or !$keys[53].IsChecked){throw 'Lost shortcut draft'}
   $null=$w.Activate()
   $null=$keys[53].Focus()
   if(!$w.IsActive){throw 'Cannot focus test keyboard'}
   try{
    foreach($code in 17,16,119){[HotkeyTestInput]::keybd_event($code,0,0,[UIntPtr]::Zero)}
    $w.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
   }finally{foreach($code in 119,16,17){[HotkeyTestInput]::keybd_event($code,0,2,[UIntPtr]::Zero)}}
   $w.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
   $labels=@($caps.Children|Where-Object {$_ -is [Windows.Controls.Border]}|ForEach-Object {$_.Child.Text})
   if(($labels -join '+') -ne 'Ctrl+Shift+F8' -or !$keys[119].IsChecked -or !$save.IsEnabled){throw "Physical key recording: $labels"}
   if($Preview){
    $panel.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$panel.ActualWidth,[int]$panel.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($panel)
    $png=New-Object Windows.Media.Imaging.PngBitmapEncoder
    $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file=[IO.File]::Create($Preview)
    try{$png.Save($file)}finally{$file.Dispose()}
   }
   Tap $keys[-4];Tap $keys[6]
   $save.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
   $w.Close()
  }catch{$script:failure=$_;if($w){$w.Close()}}
 })
 $timer.Start()
 $t.GetMethod('ShowSettings',$f).Invoke($app,@())
 if($script:failure){throw $script:failure}
 if([IO.File]::ReadAllText($path) -ne '2,6'){throw 'Saved combo'}
 '50 main keys, modifier validation, real keyboard registration, conflict retention, mouse matching and settings save passed'
}finally{
 if($allocated){$null=$unreg.Invoke($null,@($sink.Handle,11))}
 $hook=$t.GetField('mouseHook',$f).GetValue($app)
 if($hook -ne [IntPtr]::Zero){$null=$t.GetMethod('UnhookWindowsHookEx',$st).Invoke($null,@($hook))}
 [Runtime.InteropServices.Marshal]::FreeHGlobal($data);$sink.Dispose()
 if([IO.File]::Exists($path)){[IO.File]::Delete($path)}
 if([IO.Directory]::Exists($dir)){[IO.Directory]::Delete($dir)}
}
