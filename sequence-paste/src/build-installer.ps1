$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$taskProduct = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'
$taskUninstaller = Join-Path $taskProduct '卸载顺序贴.exe'
$taskInstaller = Join-Path $taskProduct '顺序贴安装包.exe'
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\Assets\app.ico" "/out:$taskUninstaller" "$PSScriptRoot\Uninstall.cs" "$PSScriptRoot\InstallCommon.cs"
if ($LASTEXITCODE -ne 0) { throw '卸载程序编译失败。' }
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\Assets\app.ico" "/resource:$PSScriptRoot\Assets\app.ico,SequencePaste.AppIcon" "/out:$taskInstaller" "/resource:$taskProduct\顺序贴.exe,payload.app" "/resource:$taskProduct\使用说明.txt,payload.help" "/resource:$taskUninstaller,payload.uninstall" "/resource:$taskProduct\顺序贴.ico,payload.icon" "$PSScriptRoot\Installer.cs" "$PSScriptRoot\InstallCommon.cs" "$PSScriptRoot\AppIcon.cs"
if ($LASTEXITCODE -ne 0) { throw '安装程序编译失败。' }
Write-Output "安装包已生成：$taskInstaller"
