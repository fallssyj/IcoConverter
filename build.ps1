$ErrorActionPreference = "Stop"


$appFolderName = "IcoConverter"
$versionFilePath = Join-Path $PSScriptRoot "src\version"
$versionPatch = (Get-Content $versionFilePath -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($versionPatch))
{
	throw "版本文件为空: $versionFilePath"
}

$version = "1.0.$versionPatch"

$projectPath = Join-Path $PSScriptRoot "src\$appFolderName.csproj"
$publishRoot = Join-Path $PSScriptRoot "src\bin\Release\publish"
$versionPropsPath = Join-Path $PSScriptRoot "src\version.props"
$updaterSourcePath = Join-Path $PSScriptRoot "src\Assets\Updagee\Updagee.exe"


function Publish-AndZip
{
	param(
		[string]$profile,
		[string]$rid,
		[string]$zipName
	)

	$zipPath = Join-Path $publishRoot $zipName
	$stagingName = "staging-$rid-$([guid]::NewGuid().ToString('N'))"
	$stagingDir = Join-Path $publishRoot $stagingName
	$publishDir = Join-Path $stagingDir $appFolderName
	$stagedZipPath = Join-Path $stagingDir $zipName

	New-Item -ItemType Directory -Path $stagingDir | Out-Null
	try
	{
		Write-Host "[开始] 发布 $rid ($profile)" -ForegroundColor Cyan
		dotnet publish $projectPath "-p:PublishProfile=$profile" "-p:PublishDir=$publishDir"
		if ($LASTEXITCODE -ne 0)
		{
			throw "发布 $rid 失败，dotnet publish 退出码为 $LASTEXITCODE。"
		}

		if (-not (Test-Path -LiteralPath (Join-Path $publishDir "$appFolderName.exe") -PathType Leaf))
		{
			throw "发布目录缺少 $appFolderName.exe：$publishDir"
		}

		Get-ChildItem -LiteralPath $publishDir -Filter "*.pdb" -File | Remove-Item -Force
		Copy-Item -LiteralPath $updaterSourcePath -Destination $publishDir -Force
		Compress-Archive -LiteralPath $publishDir -DestinationPath $stagedZipPath

		if ([IO.File]::Exists($zipPath))
		{
			[IO.File]::Replace($stagedZipPath, $zipPath, [NullString]::Value)
		}
		else
		{
			[IO.File]::Move($stagedZipPath, $zipPath)
		}
		Write-Host "[完成] 已生成压缩包: $zipPath" -ForegroundColor Green
	}
	finally
	{
		$stagingItem = Get-Item -LiteralPath $stagingDir
		$publishRootPath = (Resolve-Path -LiteralPath $publishRoot).Path
		if ($stagingItem.Parent.FullName -eq $publishRootPath -and $stagingItem.Name -eq $stagingName -and
			-not ($stagingItem.Attributes -band [IO.FileAttributes]::ReparsePoint))
		{
			Remove-Item -LiteralPath $stagingItem.FullName -Recurse -Force
		}
	}
}

Write-Host "开始执行打包脚本..." -ForegroundColor Cyan

Write-Host "[版本号]: ($version)"
@"
<Project>
	<PropertyGroup>
		<Version>$version</Version>
		<AssemblyVersion>$version.0</AssemblyVersion>
		<FileVersion>$version.0</FileVersion>
	</PropertyGroup>
</Project>
"@ | Set-Content -Path $versionPropsPath -Encoding UTF8
Write-Host "[版本] 已写入: $versionPropsPath ($version)" -ForegroundColor DarkGray
Publish-AndZip -profile "x86" -rid "x86" -zipName "$appFolderName-win-x86.zip"
Publish-AndZip -profile "x64" -rid "x64" -zipName "$appFolderName-win-x64.zip"
Write-Host "全部完成。" -ForegroundColor Green
