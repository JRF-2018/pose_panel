# VrcPoseAliasesPanel をビルドする(.NET の SDK は要らない。Unity に付いている C# のコンパイラー(Roslyn)で、
# Windows に入っている .NET Framework 4 の WinForms に向けてコンパイルする)。
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Out D:\VrcPoseAliasesPanel_test]
param([string]$Out = "$PSScriptRoot\build", [string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor")
$dotnet = "$Unity\Data\NetCoreRuntime\dotnet.exe"
$csc = "$Unity\Data\DotNetSdkRoslyn\csc.dll"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
New-Item -ItemType Directory -Force $Out | Out-Null
$refs = "mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms", "System.Web.Extensions" | ForEach-Object { "-r:$fw\$_.dll" }
$src = Get-ChildItem "$PSScriptRoot\src\*.cs" | ForEach-Object { $_.FullName }
& $dotnet $csc -nologo -nostdlib -noconfig -target:winexe -platform:anycpu -optimize+ -langversion:9 -codepage:65001 `
    "-out:$Out\VrcPoseAliasesPanel.exe" "-win32icon:$PSScriptRoot\src\panel.ico" $refs $src
exit $LASTEXITCODE
