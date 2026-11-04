git clone https://github.com/maythamfahmi/CompanyEnrichment.git
Set-Location CompanyEnrichment
docker compose --profile observability up --build
## tear down the containers after use
## docker compose down