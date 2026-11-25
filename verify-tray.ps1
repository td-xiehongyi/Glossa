param([string]$Exe='Glossa.exe',[string]$Preview)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$t=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path).GetType('TrayMenu')
$ctor=$t.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$menu=$ctor.Invoke(@([Action]{$script:action='settings'},[Action]{$script:action='exit'}))
try{
 $menu.Show([Drawing.Point]::new(100,100))
 [Windows.Forms.Application]::DoEvents()
 if(!$menu.Visible -or $menu.Items.Count -ne 2){throw 'Menu structure'}
 $menu.Items[1].Select()
 if(![Windows.Forms.SystemInformation]::HighContrast){
  $img=[Drawing.Bitmap]::new($menu.Width,$menu.Height)
  try{
   $menu.DrawToBitmap($img,$menu.ClientRectangle)
   $y=$menu.Items[1].Bounds.Top+[int]($menu.Items[1].Height/2)
   $fill=$menu.Renderer.ColorTable.MenuItemSelected.ToArgb()
   $pixels=@(0..($img.Width-1) | Where-Object {$img.GetPixel($_,$y).ToArgb() -eq $fill})
   if(!$pixels.Count){throw 'Missing highlight'}
   $left=$pixels[0];$right=$img.Width-1-$pixels[-1]
   if([Math]::Abs($left-$right) -gt 1){throw "Uneven highlight margins: left=$left right=$right"}
   "Highlight margins: left=$left right=$right"
   foreach($row in $menu.Items){
    $split=$row.Bounds.Left+[int]($menu.ImageScalingSize.Width*1.85)
    $centers=foreach($range in @(@(5,$split),@(($split+1),($img.Width-6)))){
     $ink=@(foreach($yy in ($row.Bounds.Top+2)..($row.Bounds.Bottom-3)){
      foreach($xx in $range[0]..$range[1]){
       if($img.GetPixel($xx,$yy).R -lt 120){$yy;break}
      }
     })
     if(!$ink.Count){throw "Missing icon or label for $($row.Text)"}
     ($ink[0]+$ink[-1])/2
    }
    if([Math]::Abs($centers[0]-$centers[1]) -gt 1.5){throw "Icon and label misaligned for $($row.Text): $centers"}
   }
   if($Preview){$img.Save($Preview)}
  }finally{$img.Dispose()}
 }
 foreach($test in @(@(0,'设置','settings'),@(1,'退出','exit'))){
  $item=$menu.Items[$test[0]]
  if($item.Text -ne $test[1] -or !$item.Image){throw 'Missing label or icon'}
  $item.PerformClick()
  if($script:action -ne $test[2]){throw 'Wrong menu action'}
 }
 'Rendered menu and verified both callbacks'
}finally{$menu.Dispose()}
