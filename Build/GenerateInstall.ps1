#region Settings & Error Handling
$ErrorActionPreference = "Stop"
#endregion

#region Find Tool Install Locations

# MSBuild
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found at $vswhere" }
$vspath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vspath) { throw "No Visual Studio with MSBuild found!" }
$msbuild = Join-Path $vspath 'MSBuild\15.0\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) { throw "MSBuild.exe not found at $msbuild" }

# 7-zip
$sevenZipReg = Get-ItemProperty 'HKLM:\SOFTWARE\7-Zip' -ErrorAction SilentlyContinue
if (-not $sevenZipReg) { $sevenZipReg = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\7-Zip' -ErrorAction SilentlyContinue }
if (-not $sevenZipReg) { throw "7-Zip not installed!" }
$sevenZip = Join-Path $sevenZipReg.Path "7z.exe"
if (-not (Test-Path $sevenZip)) { throw "7z.exe not found at $sevenZip" }

# NSIS
$nsisReg = Get-ItemProperty 'HKLM:\SOFTWARE\NSIS' -ErrorAction SilentlyContinue
if (-not $nsisReg) { $nsisReg = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\NSIS' -ErrorAction SilentlyContinue }
if (-not $nsisReg) { throw "NSIS not installed!" }
$nsisPath = if ($nsisReg.PSChildName -eq '(default)') { $nsisReg.'(default)' } else { $nsisReg.InstallDir }
$nsis = Join-Path $nsisPath "makensis.exe"
if (-not (Test-Path $nsis)) { throw "makensis.exe not found at $nsis" }

#endregion

#region Working Directory
$scriptdir = Split-Path $MyInvocation.MyCommand.Path
Set-Location $scriptdir
#endregion

#region Output Directory
$outDir = Join-Path $scriptdir 'Out'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item $outDir -ItemType Directory | Out-Null
$buildDir = Join-Path $outDir 'Build'
New-Item $buildDir -ItemType Directory | Out-Null
$logFile = Join-Path $buildDir 'Build.log'
Set-Content $logFile ""
#endregion

#region Compile Shaders
Write-Host "Compiling shaders..."
$shaderCompile = & "..\Sledge.Rendering\Shaders\compile-shaders.ps1"
$shaderCompile | Out-File $logFile -Append
#endregion

#region Build Project
Write-Host "Building Solution..."
$msbuildArgs = @("../Sledge.sln", "/p:Configuration=Release", "/p:OutputPath=$buildDir")
& $msbuild $msbuildArgs | Out-File $logFile -Append
#endregion

#region Clean Up
# Remove unnecessary files
# Remove-Item "$buildDir\*.pdb" -ErrorAction SilentlyContinue
Remove-Item "$buildDir\*.xml" -ErrorAction SilentlyContinue
#endregion

#region Version Information
$exePath = Join-Path $buildDir 'Sledge.Editor.exe'
if (-not (Test-Path $exePath)) { throw "Build failed, Sledge.Editor.exe not found!" }
$versionInfo = (Get-Command $exePath).FileVersionInfo
$version = $versionInfo.ProductVersion
if (-not $version) { throw "Cannot read version from $exePath" }
Write-Host "Version is $version."
#endregion

#region Archive
$zipPath = Join-Path $outDir "Sledge.Editor.$version.zip"
Write-Host "Creating Archive..."
& $sevenZip 'a' '-tzip' '-r' $zipPath "$buildDir\*.*" | Out-File $logFile -Append
#endregion

#region Installer
$nsifile = Join-Path $outDir "Sledge.Editor.Installer.$version.nsi"
Write-Host "Creating Installer..."
(Get-Content '.\Sledge.Editor.Installer.nsi') -replace '\{version\}', $version | Set-Content $nsifile
& $nsis $nsifile | Out-File $logFile -Append
#endregion

#region Version File
$verfile = Join-Path $outDir 'version.txt'
$date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$url = "https://github.com/LogicAndTrick/sledge/releases/download/$version/Sledge.Editor.$version.zip"
Set-Content $verfile "$version`n$date`n$url"
#endregion

Write-Host "Done."
