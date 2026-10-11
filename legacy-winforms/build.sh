#!/bin/bash
# grobyq build on Linux (.NET SDK 8 + mono-devel for the .NET Framework 4.8 reference assemblies).
# Usage: ./build.sh [license-list-raw-url]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/src"
URL="${1:-https://raw.githubusercontent.com/mitzuLLC/grobpc-licenses/main/licenses.json}"
PUB="$(cat "$HERE/grob_pub_spki.b64")"
REF="${REF48:-/usr/lib/mono/4.8-api}"
python3 - "$PUB" "$URL" "$SRC" <<'PY'
import sys
pub, url, src = sys.argv[1:4]
t = open(src + '/Licensing.cs.tmpl').read().replace('@@GROB_PUBKEY@@', pub).replace('@@LIST_URL@@', url)
open(src + '/Licensing.cs', 'w').write(t)
print('list url =', url)
PY
# WindowsDesktop SDK is not available on Linux: build through a plain csproj with explicit WinForms references.
TMP="$(mktemp -d)"
cat > "$TMP/grobyq.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType><TargetFramework>net48</TargetFramework><LangVersion>latest</LangVersion>
    <AssemblyName>grobyq</AssemblyName><PlatformTarget>x64</PlatformTarget><RootNamespace>Grobyq</RootNamespace>
    <ApplicationManifest>$SRC/app.manifest</ApplicationManifest><ApplicationIcon>$SRC/app.ico</ApplicationIcon>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit>
    <Product>grobyq by mitzu LLC</Product><AssemblyTitle>grobyq</AssemblyTitle><Company>mitzu LLC</Company>
    <Version>3.6.8</Version><FileVersion>3.6.8.0</FileVersion><Deterministic>true</Deterministic>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$SRC/**/*.cs" Exclude="$SRC/obj/**;$SRC/bin/**" />
    <EmbeddedResource Include="$SRC/Assets/*.png" LogicalName="%(Filename)%(Extension)" />
    <EmbeddedResource Include="$SRC/Assets/*.gz" LogicalName="%(Filename)%(Extension)" />
    <EmbeddedResource Include="$SRC/Assets/*.bin" LogicalName="%(Filename)%(Extension)" />
    <EmbeddedResource Include="$SRC/Assets/*.txt" LogicalName="%(Filename)%(Extension)" />
    <EmbeddedResource Include="$SRC/Lang/*.json" LogicalName="lang.%(Filename).json" />
    <Reference Include="System.Management" /><Reference Include="System.Numerics" /><Reference Include="System.Web.Extensions" />
    <Reference Include="System.Security" /><Reference Include="System.Windows.Forms" /><Reference Include="System.Drawing" /><Reference Include="System.ServiceProcess" />
  </ItemGroup>
</Project>
XML
mkdir -p "$TMP/feed"
dotnet build "$TMP/grobyq.csproj" -c Release -o "$HERE/out" -p:FrameworkPathOverride="$REF" -p:RestoreSources="$TMP/feed" -p:AutomaticallyUseReferenceAssemblyPackages=false | grep -E "error|warning CS| -> |Build succeeded" | grep -v "CS1701\|CA1416" || true
rm -rf "$TMP"
echo "Output: $HERE/out/grobyq.exe"
