param([string]$Exe='Glossa.exe')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,System.Drawing
$asm=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path)
$appType=$asm.GetType('App')
$application=[Activator]::CreateInstance($appType)
$application.ShutdownMode='OnExplicitShutdown'
$t=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path).GetType('GlassAppearance')
$f=[Reflection.BindingFlags]'Instance,NonPublic'
$a=[Activator]::CreateInstance($t)
$dir=[IO.Directory]::CreateDirectory([IO.Path]::Combine($env:TEMP,[guid]::NewGuid().ToString()))
$path=Join-Path $dir.FullName 'appearance.xml'
$appType.GetField('config',$f).SetValue($application,(Join-Path $dir.FullName 'hotkey.txt'))
$appType.GetField('appearance',$f).SetValue($application,$a)
$params=[Windows.Interop.HwndSourceParameters]::new('Settings preview fixture')
$params.WindowStyle=0
$sink=[Windows.Interop.HwndSource]::new($params)
$appType.GetField('sink',$f).SetValue($application,$sink)
$appType.GetField('vk',$f).SetValue($application,[uint32]65)
function Elements($root){
 $root
 foreach($child in [Windows.LogicalTreeHelper]::GetChildren($root)){
  if($child -is [Windows.DependencyObject]){Elements $child}
 }
}
$bg=New-Object Windows.Window
$bg.Topmost=$true;$bg.WindowStyle='None';$bg.Width=600;$bg.Height=320;$bg.Left=80;$bg.Top=80
$pattern=New-Object Windows.Controls.StackPanel
$pattern.Orientation='Horizontal'
foreach($i in 0..149){
 $bar=New-Object Windows.Controls.Border
 $bar.Width=4
 $bar.Background=if($i%2){[Windows.Media.Brushes]::Black}else{[Windows.Media.Brushes]::White}
 $null=$pattern.Children.Add($bar)
}
$bg.Content=$pattern
function StripeRange($face,$x=190,$y=22){
 $face.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
 Start-Sleep -Milliseconds 320
 $pt=$face.PointToScreen([Windows.Point]::new($x,$y))
 $img=[Drawing.Bitmap]::new(80,1);$g=[Drawing.Graphics]::FromImage($img)
 try{
  $g.CopyFromScreen([int]$pt.X,[int]$pt.Y,0,0,$img.Size)
  $range=0..79|ForEach-Object {$img.GetPixel($_,0).R}|Measure-Object -Minimum -Maximum
  return $range.Maximum-$range.Minimum
 }finally{$g.Dispose();$img.Dispose()}
}
try{
 $defaults=[Activator]::CreateInstance($t)
 $a.Rgb=0x456789
 $a.Blur=$false
 $t.GetMethod('Save',$f).Invoke($a,@([string]$path))
 $original=(Get-FileHash $path).Hash
 $bg.Show()
 foreach($commit in $false,$true){
  $timer=New-Object Windows.Threading.DispatcherTimer
  $timer.Interval=[TimeSpan]::FromMilliseconds(120)
  $timer.Add_Tick({
   $timer.Stop()
   $w=$application.Windows | Where-Object Title -eq 'Glossa 设置'
   $pages=$w.Content.FindName('Pages')
   $pages.SelectedIndex=1;$w.UpdateLayout()
   $w.Left=$bg.Left+20;$w.Top=$bg.Top+12;$w.UpdateLayout()
   try{
    $nodes=@(Elements $pages.Items[1].Content)
    $sliders=@{}
    foreach($node in $nodes){if($node -is [Windows.Controls.Slider]){$sliders[[Windows.Automation.AutomationProperties]::GetName($node)]=$node}}
    $face=$nodes|Where-Object Name -eq Surface
    $blur=$w.Owner
    $body=$nodes|Where-Object Name -eq Body
    $sliders['红'].Value=18;$sliders['绿'].Value=26;$sliders['蓝'].Value=42
    if($body.Foreground.Color -ne [Windows.Media.Colors]::White){throw 'dark sample text'}
    $sliders['玻璃透明度'].Value=75;$sliders['渐变强度'].Value=0;$sliders['边缘高光'].Value=35
    $sliders['圆角大小'].Value=20;$sliders['阴影强度'].Value=40
    ($nodes|Where-Object {$_ -is [Windows.Controls.CheckBox]}).IsChecked=$false
    $w.UpdateLayout()
    if($blur.IsVisible -or $face.CornerRadius.TopLeft -ne 20 -or $face.Effect.Opacity -ne 0.4 -or $face.Background.Opacity -ne 0.25 -or $face.BorderBrush.Opacity -ne 0.35){throw 'preview did not update'}
    if($body.Foreground.Color -eq [Windows.Media.Colors]::White -or $face.Background.GradientStops[0].Color.ToString() -ne '#FF121A2A'){throw 'preview tint or text'}
    $mat=$pages.Items[1].Content.FindName('PreviewMat')
    if((StripeRange $mat 12 24) -gt 5){throw 'Desktop visible outside the sample'}
    $clear=StripeRange $face
    ($nodes|Where-Object {$_ -is [Windows.Controls.CheckBox]}).IsChecked=$true
    $w.UpdateLayout()
    $frost=StripeRange $face
    "Desktop stripe contrast, clear=$clear blurred=$frost"
    if($clear -lt 90 -or $frost -gt $clear*0.45){throw "desktop blur clear=$clear frost=$frost owner=$($blur.IsVisible) position=$($blur.Left),$($blur.Top) card=$($w.Left),$($w.Top) point=$($face.PointToScreen([Windows.Point]::new(190,22)))"}
    if(!$blur.IsVisible -or [Math]::Abs($blur.Width-($face.ActualWidth-12)) -gt 1){throw 'blur footprint'}
    ($nodes|Where-Object {$_ -is [Windows.Controls.CheckBox]}).IsChecked=$false
    ($nodes|Where-Object Name -eq ResetButton).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    $w.UpdateLayout()
    if(!$blur.IsVisible -or $face.Background.GradientStops[1].Color.ToString() -ne '#FFE6F1F7' -or $face.Background.Opacity -ne 0.74 -or $face.CornerRadius.TopLeft -ne 12 -or $face.Effect.Opacity -ne 0.22 -or $face.BorderBrush.Opacity -ne 0.9 -or $sliders['渐变强度'].Value -ne $defaults.Gradient){throw 'reset preview'}
    if((Get-FileHash $path).Hash -ne $original){throw 'premature save'}
    if($commit){
     ($nodes|Where-Object {$_ -is [Windows.Controls.Button] -and $_.Content -eq '保存'}).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    }
    $w.Close()
   }catch{$script:failure=$_;if($w){$w.Close()}}
  })
  $timer.Start()
  $appType.GetMethod('ShowSettings',$f).Invoke($application,@())
  if($script:failure){throw $script:failure}
  if($a.Rgb -ne 0x456789 -or (!$commit -and (Get-FileHash $path).Hash -ne $original)){throw 'draft isolation'}
  if($commit){
   $loaded=$t.GetMethod('Load',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@([string]$path))
   foreach($field in $t.GetFields()){if($field.GetValue($loaded) -ne $field.GetValue($defaults)){throw $field.Name}}
  }
 }
 'Live preview: seven controls, blur clipping, cancel and save passed'
}finally{$bg.Close();if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path};$dir.Delete();$sink.Dispose()}

