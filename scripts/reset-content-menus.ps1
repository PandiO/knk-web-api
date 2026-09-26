<#
.SYNOPSIS
  Deletes the seeded InventoryMenu content templates so the API re-creates them from
  MenuTemplateSeed.Content.cs on its next start (the seed is create-only: it never updates a
  template that already exists).

.EXAMPLE
  .\scripts\reset-content-menus.ps1
  .\scripts\reset-content-menus.ps1 -BaseUrl http://localhost:5294 -Token <jwt>

  Then restart the API (seeds run at startup), then restart the Minecraft server (or /reload the
  plugin) so it loads and validates the fresh templates.
#>
param(
    [string]$BaseUrl = "http://localhost:5294",
    [string]$Token = "",
    [switch]$WhatIf
)

$keys = @(
    "main",
    "kits.overview",
    "profile.main",
    "items.catalog",
    "premium.tiers",
    "users.manager",
    "users.manager.edit",
    "users.manager.titles",
    "users.manager.groups"
)

$headers = @{}
if ($Token) { $headers["Authorization"] = "Bearer $Token" }

foreach ($key in $keys) {
    try {
        $template = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/MenuTemplates/by-key/$key" -Headers $headers
    } catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 404) { Write-Host "skip   $key (not in the database)"; continue }
        Write-Host "ERROR  $key : GET failed ($status) $($_.Exception.Message)" -ForegroundColor Red
        continue
    }
    if ($WhatIf) { Write-Host "would delete $key (id $($template.id))"; continue }
    try {
        Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/MenuTemplates/$($template.id)" -Headers $headers | Out-Null
        Write-Host "delete $key (id $($template.id))" -ForegroundColor Green
    } catch {
        Write-Host "ERROR  $key : DELETE failed $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done. Restart the API to re-seed these templates, then restart the Minecraft server."
