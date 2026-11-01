$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sdkVersion = '1.0.4314.35'
$sdk = Join-Path $root "build\webview2-$sdkVersion\package"
if (!(Test-Path -LiteralPath "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll")) {
    $packageRoot = Split-Path $sdk
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    $zip = Join-Path $packageRoot 'package.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $zip
    Expand-Archive $zip -DestinationPath $sdk -Force
}
$dest = Join-Path $root 'dist\com.projectdeck.trula.sdPlugin'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Path (Join-Path $root 'plugin\*') -Destination $dest -Recurse -Force
Copy-Item "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll","$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$sdk\runtimes\win-x64\native\WebView2Loader.dll" $dest
Copy-Item "$sdk\LICENSE.txt" "$dest\WEBVIEW2-LICENSE.txt"
Copy-Item "$sdk\NOTICE.txt" "$dest\WEBVIEW2-NOTICE.txt"
$references = @('/r:System.Windows.Forms.dll',"/r:$dest\Microsoft.Web.WebView2.Core.dll","/r:$dest\Microsoft.Web.WebView2.WinForms.dll")
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Security.dll $references "/out:$dest\TrulaPlugin.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& $compiler /nologo /target:exe /main:TrulaTests /platform:x64 /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Security.dll $references "/out:$dest\TrulaTests.exe" $sources (Join-Path $root 'tests\TrulaTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& (Join-Path $dest 'TrulaTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
Write-Host "Built: $dest"
