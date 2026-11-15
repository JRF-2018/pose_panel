# VRM ポーズビューアーを Unity のバッチモードでビルドする(Unity のエディターの画面は開かない)。
# Unity のプロジェクト(ビルドの素材と Library)は D:\Unity\VrmPoseViewer に置き(sync_to_d.sh がそこへ送る)、
# できたアプリ(exe と、その横に要る VrmPoseViewer_Data などのフォルダ)は D:\vrchat_osc_agent\VrmPoseViewer に書き出す。
# こうすると D:\vrchat_osc_agent には動かすのに要る物だけが残り、PC2 へはそのフォルダをコピーすればよい。
# 初回は UniVRM を GitHub から取ってくるので数分かかる。記録は build.log。
#   powershell -ExecutionPolicy Bypass -File D:\Unity\VrmPoseViewer\build.ps1
# 試しのビルドは -Product に別の名前を(設定 = レジストリの HKCU\Software\JRF\<名前> が、ふだんのものと混ざらない)。
#   build.ps1 -Out D:\vrchat_osc_agent_test\VrmPoseViewer -Product VrmPoseViewerTest
param([string]$Out = "D:\vrchat_osc_agent\VrmPoseViewer", [string]$Product = "VrmPoseViewer")
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& $unity -batchmode -quit -projectPath $here -buildTarget Win64 -executeMethod BuildViewer.Build -viewerOut "$Out\VrmPoseViewer.exe" -viewerProduct $Product -logFile "$here\build.log" | Out-Null
if ($LASTEXITCODE -eq 0) { Write-Host "ビルドできました: $Out\VrmPoseViewer.exe" }
else { Write-Host "ビルドに失敗しました(終了コード $LASTEXITCODE)。build.log を見てください。" }
exit $LASTEXITCODE
