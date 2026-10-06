<#
    Automatise la mise en place de la signature de code AUTO-SIGNÉE de SOS-LAN
    (un seul certificat, utilisé par la CI pour Windows ET macOS).

    Ce script :
      1. génère un certificat de signature de code auto-signé (RSA 3072, SHA-256) ;
      2. l'exporte en .pfx protégé par un mot de passe aléatoire, jamais affiché ;
      3. enregistre le .pfx (base64) et le mot de passe comme secrets GitHub du dépôt
         (SIGNING_CERT_PFX_BASE64 / SIGNING_CERT_PASSWORD) via la CLI "gh" ;
      4. écrit le certificat PUBLIC dans installer/SosLan-codesign.cer (à committer, utile
         pour que les postes fassent confiance aux versions signées) ;
      5. garde une sauvegarde locale dans %USERPROFILE%\SosLan-signing-backup (le mot de
         passe y est chiffré DPAPI : lisible uniquement par ce compte Windows).

    Prérequis : "gh auth login" déjà fait, avec le droit d'écrire les secrets du dépôt.

    ATTENTION : relancer ce script crée un NOUVEAU certificat (nouvelle empreinte). Les postes
    ayant approuvé l'ancien devront approuver le nouveau, et sous macOS la permission
    Accessibilité sera redemandée une fois. À ne faire qu'en cas de renouvellement/compromission.
#>
param(
    [string]$Repo = "Rimfire03/Sos-Alarme",
    [string]$Subject = "CN=Tomline Prod and Co, O=Tomline Prod and Co",
    [int]$ValidityYears = 10
)

$ErrorActionPreference = "Stop"

$repoRoot = (& git rev-parse --show-toplevel).Trim()
$cerPath = Join-Path $repoRoot "installer\SosLan-codesign.cer"
$backupDir = Join-Path $env:USERPROFILE "SosLan-signing-backup"
$tempPfx = Join-Path ([IO.Path]::GetTempPath()) ("soslan-" + [Guid]::NewGuid().ToString("N") + ".pfx")

function New-RandomPassword([int]$length = 32) {
    $chars = [char[]]"ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    $bytes = New-Object byte[] $length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

# "gh secret set" lit le secret sur l'entrée standard ; on l'écrit sans retour à la ligne final
# (qui ferait partie du secret) et sans jamais le passer en argument de commande.
function Set-GitHubSecret([string]$name, [string]$value) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = "gh"
    $psi.Arguments = "secret set $name --repo $Repo"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write($value)
    $process.StandardInput.Close()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Échec de l'enregistrement du secret $name : $($process.StandardError.ReadToEnd())"
    }
}

$passwordPlain = New-RandomPassword
$passwordSecure = ConvertTo-SecureString $passwordPlain -AsPlainText -Force

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears($ValidityYears)

try {
    Export-PfxCertificate -Cert $cert -FilePath $tempPfx -Password $passwordSecure | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    Set-GitHubSecret "SIGNING_CERT_PFX_BASE64" ([Convert]::ToBase64String([IO.File]::ReadAllBytes($tempPfx)))
    Set-GitHubSecret "SIGNING_CERT_PASSWORD" $passwordPlain

    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item $tempPfx (Join-Path $backupDir "SosLan-codesign.pfx") -Force
    $passwordSecure | ConvertFrom-SecureString | Set-Content (Join-Path $backupDir "SosLan-codesign.password.dpapi") -Encoding ascii
}
finally {
    if (Test-Path $tempPfx) { Remove-Item $tempPfx -Force }
    Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Certificat créé : $Subject"
Write-Host "  Empreinte SHA-1 : $($cert.Thumbprint)"
Write-Host "  Valable jusqu'au : $($cert.NotAfter.ToString('dd/MM/yyyy'))"
Write-Host "  Secrets GitHub  : SIGNING_CERT_PFX_BASE64, SIGNING_CERT_PASSWORD (dépôt $Repo)"
Write-Host "  Certificat public : $cerPath  (à committer)"
Write-Host "  Sauvegarde locale : $backupDir  (hors dépôt, ne jamais committer)"
