param([string]$OutputPath = 'Glossa.exe')
$ErrorActionPreference = 'Stop'
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$refs = 'PresentationCore','PresentationFramework','WindowsBase','UIAutomationClient','UIAutomationTypes' | ForEach-Object { '/r:' + (Join-Path "$fx\WPF" "$_.dll") }
Push-Location $PSScriptRoot
try {
    & "$fx\csc.exe" /nologo /utf8output /codepage:65001 /target:winexe /win32icon:app-icon.ico "/out:$OutputPath" /resource:Popup.xaml,Popup.xaml /resource:Preview.xaml,Preview.xaml /resource:Settings.xaml,Settings.xaml /resource:Hotkeys.xaml,Hotkeys.xaml /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll @refs App.cs TrayMenu.cs GlassAppearance.cs PopupGlass.cs Selection.cs LocalTranslator.cs
    if ($LASTEXITCODE) {throw '编译失败'}
} finally { Pop-Location }
