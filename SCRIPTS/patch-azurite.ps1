$accountName = "devstoreaccount1"
$accountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KkvkbzW1bk+w=="
$tableName = "PoRepoLineTrackerRepositories"
$partitionKey = "524d321c-0acb-4083-90cf-9f1cc879d5d8"
$rowKey = "694c28fd-9a9e-47cc-94a8-03857d561b4b"
$localPath = "C:\LocalRepos\local_6c94cbd6-40fa-4afe-b69a-c2a5210f10f9"

$date = [System.DateTime]::UtcNow.ToString("R")
$body = "{""LocalPath"":""$($localPath.Replace('\','\\'))""}" 
$contentType = "application/json"
$url = "http://127.0.0.1:10002/$accountName/$tableName(PartitionKey='$partitionKey',RowKey='$rowKey')"
$resource = "/$accountName/$tableName(PartitionKey='$partitionKey',RowKey='$rowKey')"
# Table Storage SharedKeyLite: StringToSign = Date + newline + CanonicalizedResource
$stringToSign = "$date`n$resource"

$keyBytes = [System.Convert]::FromBase64String($accountKey)
$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = $keyBytes
$sig = [System.Convert]::ToBase64String($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($stringToSign)))
$authHeader = "SharedKeyLite ${accountName}:${sig}"

Write-Host "Calling MERGE on: $url"
Write-Host "Body: $body"

try {
    $response = Invoke-WebRequest -Uri $url -Method MERGE `
        -Headers @{
            "Authorization" = $authHeader
            "x-ms-date" = $date
            "x-ms-version" = "2019-02-02"
            "Accept" = "application/json;odata=nometadata"
        } `
        -Body $body -ContentType $contentType -UseBasicParsing
    Write-Host "Status: $($response.StatusCode)"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Response body: $($reader.ReadToEnd())"
    }
}
