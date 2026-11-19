# WinWinget.exe'yi derler (tüm widget'lar tek uygulamada).
# Windows'ta kurulu .NET Framework derleyicisi yeterli (C# 5). Kullanım: ./build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

Get-Process WinWinget -ErrorAction SilentlyContinue | Stop-Process -Force
$sources = Get-ChildItem 'src' -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /out:WinWinget.exe `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz" }
Get-Item WinWinget.exe | Select-Object Name, Length
