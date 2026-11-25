param([string]$Exe='Glossa.exe')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$asm=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path)
$t=$asm.GetType('GlassAppearance')
$a=[Activator]::CreateInstance($t)
$f=[Reflection.BindingFlags]'Instance,NonPublic'
$load=$t.GetMethod('Load',[Reflection.BindingFlags]'Static,NonPublic')
$path=Join-Path $env:TEMP ([guid]::NewGuid().ToString()+'.xml')
try{
 $a.Rgb=0x221144;$a.Transparency=30;$a.Blur=$false;$a.Gradient=0;$a.Highlight=55;$a.Radius=18;$a.Shadow=40
 $save=$t.GetMethod('Save',$f)
 $save.Invoke($a,@([string]$path));$save.Invoke($a,@([string]$path))
 $b=$load.Invoke($null,@([string]$path))
 foreach($field in $t.GetFields()){if($field.GetValue($a) -ne $field.GetValue($b)){throw $field.Name}}
 $appType=$asm.GetType('App')
 $app=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($appType)
 $appType.GetField('appearance',$f).SetValue($app,$b)
 $appType.GetMethod('BuildCard',$f).Invoke($app,@())
 $w=$appType.GetField('card',$f).GetValue($app)
 try{
  $face=$w.Content.FindName('Surface');$tip=$w.Content.FindName('Tip')
  if($w.Owner -or $face.Background.Opacity -ne 0.7 -or $face.CornerRadius.TopLeft -ne 18 -or $face.BorderBrush.Opacity -ne 0.55 -or $face.Effect.Opacity -ne 0.4){throw 'Surface settings'}
  foreach($stop in $face.Background.GradientStops){if($stop.Color.ToString() -ne '#FF221144'){throw 'Flat tint'}}
  if($tip.Fill.Color.ToString() -ne '#FF221144' -or $tip.Fill.Opacity -ne 0.7){throw 'Tip tint'}
 }finally{$w.Close()}
 $a.Transparency=101;$save.Invoke($a,@([string]$path))
 if($load.Invoke($null,@([string]$path)).Transparency -ne 26){throw 'Invalid value fallback'}
 'Seven settings persisted, replacement save, popup styling and invalid value fallback passed'
}finally{if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}


