# Run on a Windows host of the RID being tested. Requires .NET 10 SDK and NuGet restore access.
# No graphical desktop is required. Does not certify window creation or rendered frames.
param([string]$Rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier)
$ErrorActionPreference = 'Stop'
if ($Rid -notin @('win-x64', 'win-arm64')) { throw 'Use a matching win-x64 or win-arm64 host.' }
if ($Rid -ne [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier) { throw 'The requested RID must match this host.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $repo 'obj/phase1-native-smoke'
New-Item -ItemType Directory -Force $work | Out-Null
$driver = [System.Security.SecurityElement]::Escape((Join-Path $repo 'src/TSharpVision.Drivers.SDL/TSharpVision.Drivers.SDL.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><ProjectReference Include="$driver" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $work 'probe.csproj')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ShadercrossProbe.cs') -Destination (Join-Path $work 'Program.cs')
$project = Join-Path $work 'probe.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE) { throw 'Probe build failed.' }
dotnet publish $project -c Release --self-contained false -o (Join-Path $work 'publish-fdd')
if ($LASTEXITCODE) { throw 'Framework-dependent publish failed.' }
dotnet publish $project -c Release -r $Rid --self-contained false -o (Join-Path $work 'publish-rid')
if ($LASTEXITCODE) { throw 'RID publish failed.' }
$previous = $env:NUGET_PACKAGES
try {
    $layouts = @('bin/Release/net10.0', 'publish-fdd', 'publish-rid')
    foreach ($layout in $layouts) {
        $dll = Join-Path $work "$layout/probe.dll"
        $env:NUGET_PACKAGES = Join-Path $work 'absent-cache'
        $first = @(dotnet $dll)
        if ($LASTEXITCODE) { throw "Native probe failed: $layout" }
        # Fake higher and lower versions must never participate in runtime selection.
        $env:NUGET_PACKAGES = Join-Path $work 'unrelated-cache'
        foreach ($version in @('0.0.1', '99.99.99')) {
            $native = Join-Path $env:NUGET_PACKAGES "sdl3-cs.windows.shadercross/$version/runtimes/$Rid/native"
            New-Item -ItemType Directory -Force $native | Out-Null
            'not a library' | Set-Content (Join-Path $native 'SDL3_shadercross.dll')
        }
        $second = @(dotnet $dll)
        if ($LASTEXITCODE) { throw "Unrelated versions affected loading: $layout" }
        if (($first -join [Environment]::NewLine) -cne ($second -join [Environment]::NewLine)) { throw "Non-deterministic loading: $layout" }
        Write-Output "PASS $layout (absent and unrelated caches)"
        $first
    }
}
finally { $env:NUGET_PACKAGES = $previous }
