param($WebhookUrl)

echo "run: Publishing app binaries"

Push-Location 

& dotnet publish "$PSScriptRoot/src/Seq.App.GoogleChat" -c Release -o "$PSScriptRoot/src/Seq.App.GoogleChat/obj/publish" --version-suffix=local

if($LASTEXITCODE -ne 0) { exit 1 }    

echo "run: Piping live Seq logs to the app"

& seqcli tail --json | & seqcli app run -d "$PSScriptRoot/src/Seq.App.GoogleChat/obj/publish" -p WebhookUrl="$WebhookUrl" 2>&1 | & seqcli print
