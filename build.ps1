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

	$publishDir = Join-Path $publishRoot "win-$rid\$appFolderName"
	$zipPath = Join-Path $publishRoot $zipName
	$ridRoot = Join-Path $publishRoot "win-$rid"

	Write-Host "[开始] 发布 $rid ($profile)" -ForegroundColor Cyan

	dotnet publish $projectPath -p:PublishProfile=$profile

	Get-ChildItem -Path $publishDir -Filter "*.pdb" -File | Remove-Item -Force
	Write-Host "[完成] 已清理 PDB: $publishDir" -ForegroundColor DarkGray

	if (-not (Test-Path $updaterSourcePath))
    {
        throw "找不到更新程序: $updaterSourcePath"
    }

    Copy-Item $updaterSourcePath -Destination $publishDir -Force
    Write-Host "[完成] 已复制更新程序: $publishDir" -ForegroundColor DarkGray

	if (Test-Path $zipPath)
	{
		Remove-Item $zipPath -Force
	}

	Compress-Archive -Path (Join-Path $ridRoot "*") -DestinationPath $zipPath
	Write-Host "[完成] 已生成压缩包: $zipPath" -ForegroundColor Green

	if (Test-Path $ridRoot)
	{
		Remove-Item $ridRoot -Recurse -Force
		Write-Host "[清理] 已删除目录: $ridRoot" -ForegroundColor DarkGray
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
