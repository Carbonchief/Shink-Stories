[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0-android"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Shink.Mobile\Shink.Mobile.csproj"
$keyStore = if ($env:SCHINK_ANDROID_PLAY_UPLOAD_KEYSTORE) {
    $env:SCHINK_ANDROID_PLAY_UPLOAD_KEYSTORE
} else {
    Join-Path $HOME ".android\schink-stories-play-upload.keystore"
}
$keyAlias = if ($env:SCHINK_ANDROID_PLAY_UPLOAD_KEY_ALIAS) {
    $env:SCHINK_ANDROID_PLAY_UPLOAD_KEY_ALIAS
} else {
    "schink-stories-play-upload"
}
$credentialTarget = if ($env:SCHINK_ANDROID_PLAY_UPLOAD_CREDENTIAL_TARGET) {
    $env:SCHINK_ANDROID_PLAY_UPLOAD_CREDENTIAL_TARGET
} else {
    "Schink Stories Google Play Upload Key"
}
$appIcon = Join-Path $repoRoot "Shink.Mobile\Resources\AppIcon\schink_appicon.png"
$playStoreIcon = Join-Path $repoRoot "Shink.Mobile\Resources\AppIcon\schink_appicon_playstore.png"
$expectedSourceIconHash = "30faba4a58e01bf90b4fdd3580308312aca40a5e93e9298dcbf34fd1f9e8eba8"
$expectedAndroidIconHash = "648f5a7e5de3bf9d956d8af8c6428f3edac9d4e49dcd33d0254b639cb316476c"

foreach ($iconPath in @($appIcon, $playStoreIcon)) {
    if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
        throw "Missing approved teal app icon: $iconPath"
    }

    $iconHash = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($iconHash -ne $expectedSourceIconHash) {
        throw "App icon does not match the approved teal Schink icon: $iconPath"
    }
}

if (-not (Test-Path -LiteralPath $keyStore -PathType Leaf)) {
    throw "Missing Google Play upload keystore: $keyStore"
}

if (-not ("Schink.NativeCredential" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace Schink
{
    public static class NativeCredential
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPointer);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        public static string ReadGenericPassword(string target)
        {
            IntPtr credentialPointer;
            if (!CredRead(target, 1, 0, out credentialPointer))
            {
                throw new InvalidOperationException("Windows Credential Manager entry was not found: " + target);
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPointer);
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }
    }
}
"@
}

$keyPassword = [Schink.NativeCredential]::ReadGenericPassword($credentialTarget)

dotnet restore $projectPath `
    -p:TargetFramework=$Framework `
    -p:SchinkGooglePlayBuild=true `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Google Play bundle restore failed with exit code $LASTEXITCODE."
}

dotnet clean $projectPath `
    --framework $Framework `
    --configuration $Configuration `
    -p:SchinkGooglePlayBuild=true `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Google Play bundle clean failed with exit code $LASTEXITCODE."
}

dotnet publish $projectPath `
    --framework $Framework `
    --configuration $Configuration `
    -p:AndroidPackageFormat=aab `
    -p:SchinkGooglePlayBuild=true `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Google Play bundle build failed with exit code $LASTEXITCODE."
}

$unsignedBundle = Join-Path $repoRoot "Shink.Mobile\bin\$Configuration\$Framework\publish\com.schink.stories.mobile.aab"
if (-not (Test-Path -LiteralPath $unsignedBundle -PathType Leaf)) {
    throw "Unsigned Google Play bundle was not produced: $unsignedBundle"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundleArchive = [System.IO.Compression.ZipFile]::OpenRead($unsignedBundle)
try {
    $iconEntry = $bundleArchive.GetEntry("base/res/mipmap-xxxhdpi-v4/schink_appicon.png")
    if (-not $iconEntry) {
        throw "Google Play bundle is missing the generated launcher icon."
    }

    $iconStream = $iconEntry.Open()
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $embeddedIconHash = ([BitConverter]::ToString($sha256.ComputeHash($iconStream))).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $iconStream.Dispose()
    }

    if ($embeddedIconHash -ne $expectedAndroidIconHash) {
        throw "Google Play bundle contains a stale or unexpected launcher icon."
    }
}
finally {
    $bundleArchive.Dispose()
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$applicationVersion = $project.Project.PropertyGroup.ApplicationVersion | Select-Object -First 1
if (-not $applicationVersion) {
    throw "Could not read ApplicationVersion from $projectPath"
}

$artifactDirectory = Join-Path $repoRoot "artifacts\mobile-play"
$artifactPath = Join-Path $artifactDirectory "schink-stories-mobile-v$applicationVersion-Signed.aab"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
Copy-Item -LiteralPath $unsignedBundle -Destination $artifactPath -Force

try {
    $env:SCHINK_ANDROID_PLAY_KEY_PASSWORD = $keyPassword
    jarsigner `
        -keystore $keyStore `
        -storetype PKCS12 `
        -storepass:env SCHINK_ANDROID_PLAY_KEY_PASSWORD `
        -keypass:env SCHINK_ANDROID_PLAY_KEY_PASSWORD `
        -sigalg SHA256withRSA `
        -digestalg SHA-256 `
        $artifactPath `
        $keyAlias
    if ($LASTEXITCODE -ne 0) {
        throw "Google Play bundle signing failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:SCHINK_ANDROID_PLAY_KEY_PASSWORD -ErrorAction SilentlyContinue
    $keyPassword = $null
}

if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
    throw "Signed Google Play bundle was not produced: $artifactPath"
}

Write-Output "Google Play bundle: $artifactPath"
