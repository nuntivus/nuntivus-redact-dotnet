param([ValidateSet('restore','build','test','pack','verify','format-check')][string]$Task = 'verify')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
Push-Location $root
try {
	function Invoke-Dotnet([string[]]$Arguments) {
		& dotnet @Arguments
		if ($LASTEXITCODE -ne 0) { throw "dotnet verification failed with exit code $LASTEXITCODE" }
	}
	if ($Task -eq 'format-check') {
		Invoke-Dotnet @('format', 'NuntivusRedact.slnx', '--verify-no-changes', '--no-restore')
	} else {
		Invoke-Dotnet @('restore', 'NuntivusRedact.slnx')
		if ($Task -ne 'restore') { Invoke-Dotnet @('build', 'NuntivusRedact.slnx', '-c', 'Release', '--no-restore') }
		if ($Task -in @('test', 'verify')) { Invoke-Dotnet @('test', 'NuntivusRedact.slnx', '-c', 'Release', '--no-build', '--logger', 'trx', '--results-directory', 'artifacts/test-results') }
		if ($Task -in @('pack', 'verify')) { Invoke-Dotnet @('pack', 'NuntivusRedact.slnx', '-c', 'Release', '--no-build', '-o', 'artifacts/packages') }
	}
} finally { Pop-Location }
