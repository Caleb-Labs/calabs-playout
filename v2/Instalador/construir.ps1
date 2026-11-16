# Arma el instalador: publica los programas (autocontenidos), prepara la carpeta "staging" y compila con Inno Setup 6.
# Uso:  .\construir.ps1 -Motor "C:\ruta\a\CasparCG-2.5.1"      (CasparCG se descarga aparte, de su pagina oficial)
param([Parameter(Mandatory = $true)][string]$Motor)
$ErrorActionPreference = 'Stop'
$inst = $PSScriptRoot; $proy = Split-Path $inst; $st = Join-Path $inst 'staging'
if (Test-Path $st) { Remove-Item $st -Recurse -Force }
New-Item -ItemType Directory "$st\bin" | Out-Null
foreach ($p in 'Programador','Supervisor','Interfaz') {
  dotnet publish "$proy\$p" -c Release -r win-x64 --self-contained true -o "$st\bin" -p:DebugType=none -p:DebugSymbols=false --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion de $p" }
}
Copy-Item "$proy\ui" "$st\ui" -Recurse
robocopy $Motor "$st\motor" /E /XD cef-cache /XF casparcg.config casparcg-scanner*.exe *.exp *.lib *.bat /NFL /NDL /NJH /NJS /NP | Out-Null
Copy-Item "$proy\config.ejemplo.json" "$st\config.json"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'No se encontro Inno Setup 6' }
& $iscc "$inst\CaLabs-Playout.iss"
if ($LASTEXITCODE -ne 0) { throw 'Fallo Inno Setup' }
