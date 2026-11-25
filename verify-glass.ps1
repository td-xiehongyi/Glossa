param([string]$Exe='Glossa.exe')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,System.Drawing
$t=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path).GetType('App')
$f=[Reflection.BindingFlags]'Instance,NonPublic'
$app=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($t)
$t.GetMethod('BuildCard',$f).Invoke($app,@())
$card=$t.GetField('card',$f).GetValue($app)
$bg=[Windows.Window]::new()
$bg.WindowStyle='None';$bg.ResizeMode='NoResize';$bg.Width=520;$bg.Height=240;$bg.Left=80;$bg.Top=60;$bg.Topmost=$true;$bg.ShowActivated=$false
$pattern=[Windows.Controls.StackPanel]::new();$pattern.Orientation='Horizontal';$bg.Content=$pattern
foreach($i in 0..129){$bar=[Windows.Controls.Border]::new();$bar.Width=4;$null=$pattern.Children.Add($bar)}
function Paint($mode){
 for($i=0;$i -lt $pattern.Children.Count;$i++){
 $pattern.Children[$i].Background=if($mode -eq 'white' -or ($mode -eq 'stripes' -and $i%2)){[Windows.Media.Brushes]::White}else{[Windows.Media.Brushes]::Black}}
 $bg.UpdateLayout()
 $bg.Dispatcher.Invoke([action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
 Start-Sleep -Milliseconds 450
}
function Sample($element,$x,$y){
 $pt=$element.PointToScreen([Windows.Point]::new($x,$y))
 $img=[Drawing.Bitmap]::new(80,1);$g=[Drawing.Graphics]::FromImage($img)
 try{$g.CopyFromScreen([int]$pt.X,[int]$pt.Y,0,0,$img.Size)
 $values=0..79|ForEach-Object {$img.GetPixel($_,0).R}
 $m=$values|Measure-Object -Minimum -Maximum -Average
 return @{mean=$m.Average;range=$m.Maximum-$m.Minimum}
 }finally{$g.Dispose();$img.Dispose()}
}
try{
 $bg.Show()
 $anchor=$bg.PointToScreen([Windows.Point]::new(75,45))
 $t.GetField('anchor',$f).SetValue($app,$anchor)
 $card.Content.FindName('Body').Text='译文清晰'
 $card.Show();$card.UpdateLayout()
 foreach($mode in 'white','black','stripes'){
 Paint $mode
 $sample=Sample $card 170 38
 "$mode mean=$($sample.mean) range=$($sample.range)"
 switch($mode){white{$white=$sample.mean} black{$black=$sample.mean} stripes{$range=$sample.range}}
 }
 $edge=Sample $card 170 6
 if($edge.range -lt 180){throw "Frost outside pointer: range=$($edge.range)"}
 $outside=Sample $bg 170 180
 "background stripe range=$($outside.range)"
 $body=$card.Content.FindName('Body')
 $ink=0
 foreach($row in 2..22){$line=Sample $body 0 $row; $ink=[Math]::Max($ink,$line.range)}
 if($ink -lt 95){throw "Low foreground text contrast: $ink"}
 $card.Hide()
 if($card.Owner.IsVisible){throw 'Backdrop remained visible after dismissal'}
 $card.Show();$card.UpdateLayout()
 if(!$card.Owner.IsVisible){throw 'Backdrop missing after reopening'}
 "foreground text range=$ink; outside tip range=$($edge.range); hide/reopen passed"
 if($outside.range -lt 180 -or $white-$black -lt 12 -or $range -gt ($white-$black)*0.45){throw 'Live background blur verification failed'}
}finally{$card.Close();$bg.Close()}
