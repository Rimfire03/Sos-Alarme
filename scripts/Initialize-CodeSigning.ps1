<#
    Met en place la signature de code des releases de SOS-LAN (Windows ET macOS) : enregistre le
    certificat de signature comme secrets du dépôt GitHub utilisés par la CI
    (SIGNING_CERT_PFX_BASE64 / SIGNING_CERT_PASSWORD) et copie le certificat PUBLIC dans
    installer/TomLine-signature.cer.

    Deux modes :

    1) Certificat existant de l'éditeur (recommandé : un seul certificat pour tous les projets,
       les postes ne l'approuvent qu'une fois) :

         .\scripts\Initialize-CodeSigning.ps1 -ImportFrom Rimfire03/TomLine-signing-keys `
             -PasswordFile "$env:USERPROFILE\.tomline-signing\TomLine-signature.pfx.password.txt"

       Le .pfx et le .cer sont téléchargés depuis ce dépôt privé (via "gh"), l'ouverture du .pfx
       est vérifiée et son empreinte comparée à celle du .cer. Sans -PasswordFile, le mot de
       passe est saisi masqué. Il n'est jamais affiché ni écrit sur disque.

    2) Nouveau certificat auto-signé (aucun -ImportFrom) : génère un certificat avec un mot de
       passe aléatoire et garde une sauvegarde locale hors dépôt dans
       %USERPROFILE%\SosLan-signing-backup (mot de passe chiffré DPAPI).

    Prérequis : "gh auth login" déjà fait, avec le droit d'écrire les secrets du dépôt.
#>
param(
    [string]$Repo = "Rimfire03/Sos-Alarme",
    [string]$ImportFrom,
    [string]$PfxName = "TomLine-signature.pfx",
    [string]$CerName = "TomLine-signature.cer",
    [string]$PasswordFile,
    [string]$Subject = "CN=TomLine prod&co, O=TomLine prod&co",
    [int]$ValidityYears = 5
)

$ErrorActionPreference = "Stop"

$repoRoot = (& git rev-parse --show-toplevel).Trim()
$cerOutPath = Join-Path $repoRoot "installer\TomLine-signature.cer"
$workDir = Join-Path ([IO.Path]::GetTempPath()) ("soslan-sign-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

function Get-RepoFile([string]$repo, [string]$name, [string]$destination) {
    $json = & gh api "repos/$repo/contents/$name" | ConvertFrom-Json
    [IO.File]::WriteAllBytes($destination, [Convert]::FromBase64String(($json.content -replace "\s", "")))
}

# Lit un fichier texte contenant un mot de passe, quel que soit son encodage (UTF-16 avec BOM,
# UTF-8 avec ou sans BOM, ANSI), sans jamais l'afficher.
function Read-PasswordFile([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $text = [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    } elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    } else {
        $text = [Text.Encoding]::UTF8.GetString($bytes)
    }
    $text.Trim()
}

function ConvertTo-PlainText([Security.SecureString]$secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function New-RandomPassword([int]$length = 32) {
    $chars = [char[]]"ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    $bytes = New-Object byte[] $length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

# "gh secret set" lit le secret sur l'entrée standard. Le StreamWriter de Process écrit d'office le
# préambule (BOM) de Console.InputEncoding en tête du flux, ce qui corrompait le secret (base64
# invalide). On impose donc temporairement un encodage UTF-8 SANS préambule et on écrit les octets
# bruts, sans retour à la ligne final.
function Set-GitHubSecret([string]$name, [string]$value) {
    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    $previousEncoding = [Console]::InputEncoding
    [Console]::InputEncoding = $utf8NoBom
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.FileName = "gh"
        $psi.Arguments = "secret set $name --repo $Repo"
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardError = $true
        $psi.RedirectStandardOutput = $true
        $psi.UseShellExecute = $false
        $process = [Diagnostics.Process]::Start($psi)
        $bytes = $utf8NoBom.GetBytes($value)
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.BaseStream.Flush()
        $process.StandardInput.Close()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Échec de l'enregistrement du secret $name : $($process.StandardError.ReadToEnd())"
        }
    }
    finally {
        [Console]::InputEncoding = $previousEncoding
    }
}

try {
    if ($ImportFrom) {
        $pfxPath = Join-Path $workDir $PfxName
        $cerSource = Join-Path $workDir $CerName
        Get-RepoFile $ImportFrom $PfxName $pfxPath
        Get-RepoFile $ImportFrom $CerName $cerSource

        if ($PasswordFile) {
            if (-not (Test-Path $PasswordFile)) { throw "Fichier de mot de passe introuvable : $PasswordFile" }
            $passwordPlain = Read-PasswordFile $PasswordFile
        } else {
            $passwordPlain = ConvertTo-PlainText (Read-Host "Mot de passe du fichier .pfx" -AsSecureString)
        }

        # Vérifie que le mot de passe ouvre bien le .pfx et que c'est le certificat annoncé.
        try {
            $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $passwordPlain)
        } catch {
            throw "Impossible d'ouvrir le .pfx avec ce mot de passe."
        }
        if (-not $cert.HasPrivateKey) { throw "Le .pfx ne contient pas de clé privée." }
        $publicCert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($cerSource)
        if ($publicCert.Thumbprint -ne $cert.Thumbprint) {
            throw "L'empreinte du .pfx ($($cert.Thumbprint)) ne correspond pas à celle du .cer ($($publicCert.Thumbprint))."
        }
        if ($cert.NotAfter -lt (Get-Date)) { throw "Le certificat a expiré le $($cert.NotAfter.ToString('dd/MM/yyyy'))." }

        Copy-Item $cerSource $cerOutPath -Force
    }
    else {
        $passwordPlain = New-RandomPassword
        $passwordSecure = ConvertTo-SecureString $passwordPlain -AsPlainText -Force
        $pfxPath = Join-Path $workDir "SosLan-codesign.pfx"

        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Subject `
            -KeyAlgorithm RSA -KeyLength 3072 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears($ValidityYears)

        try {
            Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $passwordSecure | Out-Null
            Export-Certificate -Cert $cert -FilePath $cerOutPath | Out-Null
        } finally {
            Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
        }

        $backupDir = Join-Path $env:USERPROFILE "SosLan-signing-backup"
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        Copy-Item $pfxPath (Join-Path $backupDir "SosLan-codesign.pfx") -Force
        $passwordSecure | ConvertFrom-SecureString | Set-Content (Join-Path $backupDir "SosLan-codesign.password.dpapi") -Encoding ascii
    }

    Set-GitHubSecret "SIGNING_CERT_PFX_BASE64" ([Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)))
    Set-GitHubSecret "SIGNING_CERT_PASSWORD" $passwordPlain

    Write-Host ""
    Write-Host "Certificat : $($cert.Subject)"
    Write-Host "  Empreinte SHA-1  : $($cert.Thumbprint)"
    Write-Host "  Valable jusqu'au : $($cert.NotAfter.ToString('dd/MM/yyyy'))"
    Write-Host "  Secrets GitHub   : SIGNING_CERT_PFX_BASE64, SIGNING_CERT_PASSWORD (dépôt $Repo)"
    Write-Host "  Certificat public : $cerOutPath  (à committer)"
}
finally {
    if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force }
}
