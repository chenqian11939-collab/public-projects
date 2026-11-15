$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$taskDist = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
$taskOutput = Join-Path $taskDist '顺序贴.exe'
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\Assets\app.ico" "/resource:$PSScriptRoot\Assets\app.ico,SequencePaste.AppIcon" "/out:$taskOutput" "$PSScriptRoot\SequencePaste.cs" "$PSScriptRoot\MiniQueueForm.cs" "$PSScriptRoot\AppIcon.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets\app.ico') -Destination (Join-Path $taskDist '顺序贴.ico') -Force
Write-Output "编译完成：$taskOutput"

Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '使用说明.txt') -Destination (Join-Path $taskDist '使用说明.txt') -Force
